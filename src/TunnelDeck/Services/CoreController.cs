using System.Diagnostics;
using System.IO;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

public sealed class CoreStatusEventArgs : EventArgs
{
    public ConnectionStatus Status { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Orchestrates the two processes that make per-app VPN work WITHOUT touching the
/// system routing table (so other apps / online games are never disrupted):
///
///   1. sing-box in proxy mode  — a local SOCKS/HTTP proxy that forwards to the VPN.
///   2. ProxiFyre               — redirects ONLY the selected apps into that proxy
///                                (via the Windows Packet Filter driver).
///
/// Connecting/disconnecting just starts/stops these local processes — no routes or
/// DNS of the whole system are changed.
/// </summary>
public sealed class CoreController
{
    private readonly object _gate = new();
    private readonly ProxiFyreController _pf = new();

    private Process? _sb;
    private bool _wantRunning;
    private int _restartAttempts;
    private const int MaxRestartAttempts = 5;

    private ServerConfig? _server;
    private IReadOnlyList<TunneledApp> _apps = Array.Empty<TunneledApp>();
    private IReadOnlyList<string> _sites = Array.Empty<string>();
    private AppSettings _settings = new();
    private string? _currentServerId;

    public event EventHandler<CoreStatusEventArgs>? StatusChanged;
    public event EventHandler<string>? LogLine;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public bool IsRunning
    {
        get { lock (_gate) return _sb is { HasExited: false }; }
    }

    public async Task StartAsync(ServerConfig server, IReadOnlyList<TunneledApp> apps, AppSettings settings,
        IReadOnlyList<string>? sites = null)
    {
        _server = server;
        _apps = apps.ToList();                                   // snapshot (avoid aliasing state lists)
        _sites = (sites ?? Array.Empty<string>()).ToList();
        _settings = settings;
        _currentServerId = server.Id;

        if (!ProxiFyreBootstrapper.IsDriverInstalled)
        {
            SetStatus(ConnectionStatus.Error,
                "Драйвер Windows Packet Filter не установлен. Переустановите TunnelDeck через установщик.");
            return;
        }
        if (!File.Exists(Paths.SingBoxExe))
        {
            SetStatus(ConnectionStatus.Error, "Ядро sing-box не установлено.");
            return;
        }
        if (!ProxiFyreBootstrapper.IsInstalled)
        {
            SetStatus(ConnectionStatus.Error, "ProxiFyre не установлен.");
            return;
        }

        await StopAsync();
        KillStraySingBox();
        ProxiFyreController.KillStray();

        _wantRunning = true;
        _restartAttempts = 0;
        SetStatus(ConnectionStatus.Connecting, $"Подключение к {server.Name}…");
        LaunchSingBox();

        _ = Task.Run(async () =>
        {
            await Task.Delay(1800);
            lock (_gate)
            {
                if (!_wantRunning || _sb is not { HasExited: false } || Status != ConnectionStatus.Connecting)
                    return;
            }
            try
            {
                _pf.WriteConfig(_apps, _sites);
                _pf.Start();
                SetStatus(ConnectionStatus.Connected, $"{Loc.T("S.St.Connected")} · {_server?.Name}");
            }
            catch (Exception ex)
            {
                SetStatus(ConnectionStatus.Error, "ProxiFyre: " + ex.Message);
            }
        });
    }

    public Task StopAsync()
    {
        _wantRunning = false;

        try { _pf.Stop(); } catch { }

        Process? proc;
        lock (_gate) { proc = _sb; _sb = null; }
        if (proc is not null)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); proc.WaitForExit(4000); }
            catch { }
            finally { proc.Dispose(); }
        }

        SetStatus(ConnectionStatus.Disconnected, "Отключено");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Apply a config change while connected. Because nothing touches system routes,
    /// this never disrupts other apps. If only the app list changed we just rewrite
    /// ProxiFyre's config and restart it; if the server changed we restart everything.
    /// </summary>
    public async Task ApplyAsync(ServerConfig server, IReadOnlyList<TunneledApp> apps, AppSettings settings,
        IReadOnlyList<string>? sites = null)
    {
        if (!IsRunning && !_wantRunning) return;

        var siteList = (sites ?? Array.Empty<string>()).ToList();

        // Adding/removing a site changes the sing-box config (split inbound), so the
        // core must restart too; an app-only change just restarts ProxiFyre.
        var sitesChanged = !_sites.SequenceEqual(siteList, StringComparer.OrdinalIgnoreCase);
        if (server.Id != _currentServerId || sitesChanged)
        {
            await StartAsync(server, apps, settings, siteList);
            return;
        }

        _apps = apps.ToList();
        _settings = settings;
        try
        {
            _pf.WriteConfig(_apps, _sites);
            _pf.Start(); // restart ProxiFyre with the new app list (no route changes)
        }
        catch (Exception ex)
        {
            SetStatus(ConnectionStatus.Error, "ProxiFyre: " + ex.Message);
        }
    }

    private void LaunchSingBox()
    {
        var config = SingBoxConfigBuilder.BuildProxyMode(_server!, _settings, _sites);
        File.WriteAllText(Paths.GeneratedConfig, config);

        var psi = new ProcessStartInfo
        {
            FileName = Paths.SingBoxExe,
            Arguments = $"run -c \"{Paths.GeneratedConfig}\" -D \"{Paths.CoreDir}\"",
            WorkingDirectory = Paths.CoreDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => OnOutput(e.Data);
        proc.ErrorDataReceived += (_, e) => OnOutput(e.Data);
        proc.Exited += OnSingBoxExited;

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        lock (_gate) _sb = proc;
    }

    private void OnOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        LogLine?.Invoke(this, line);
        try { File.AppendAllText(Paths.LogFile, line + Environment.NewLine); } catch { }

        if (line.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("configuration error", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(ConnectionStatus.Error, line);
        }
    }

    private void OnSingBoxExited(object? sender, EventArgs e)
    {
        if (!_wantRunning) return;

        // Restarting sing-box does NOT change system routes, so this is safe for other apps.
        if (_settings.KillSwitch && _restartAttempts < MaxRestartAttempts)
        {
            _restartAttempts++;
            SetStatus(ConnectionStatus.Reconnecting, $"Ядро перезапускается ({_restartAttempts}/{MaxRestartAttempts})…");
            try { LaunchSingBox(); }
            catch (Exception ex) { SetStatus(ConnectionStatus.Error, ex.Message); }
        }
        else
        {
            _wantRunning = false;
            try { _pf.Stop(); } catch { }
            SetStatus(ConnectionStatus.Error, "Ядро неожиданно остановилось.");
        }
    }

    private static void KillStraySingBox()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("sing-box"))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path is null || string.Equals(path, Paths.SingBoxExe, StringComparison.OrdinalIgnoreCase))
                        p.Kill(entireProcessTree: true);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
    }

    private void SetStatus(ConnectionStatus status, string? message)
    {
        Status = status;
        StatusChanged?.Invoke(this, new CoreStatusEventArgs { Status = status, Message = message });
    }
}
