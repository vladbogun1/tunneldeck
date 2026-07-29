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
/// Supervises the sing-box process: writes the generated config, starts/stops the
/// core, captures its output, and — when the kill-switch is on — auto-restarts the
/// core if it dies unexpectedly (minimizing the window where tunneled apps could
/// leak because the TUN interface is gone).
/// </summary>
public sealed class CoreController
{
    private readonly object _gate = new();
    private Process? _process;
    private bool _wantRunning;
    private int _restartAttempts;
    private const int MaxRestartAttempts = 5;

    public event EventHandler<CoreStatusEventArgs>? StatusChanged;
    public event EventHandler<string>? LogLine;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    private ServerConfig? _server;
    private IReadOnlyList<TunneledApp> _apps = Array.Empty<TunneledApp>();
    private AppSettings _settings = new();

    public bool IsRunning
    {
        get { lock (_gate) return _process is { HasExited: false }; }
    }

    /// <summary>Generate config from the given state and (re)start the core.</summary>
    public async Task StartAsync(ServerConfig server, IReadOnlyList<TunneledApp> apps, AppSettings settings)
    {
        _server = server;
        _apps = apps;
        _settings = settings;

        if (!File.Exists(Paths.SingBoxExe))
            throw new FileNotFoundException("sing-box core is not installed.", Paths.SingBoxExe);

        await StopAsync();
        KillStrayCores();

        _wantRunning = true;
        _restartAttempts = 0;
        SetStatus(ConnectionStatus.Connecting, $"Подключение к {server.Name}…");
        LaunchProcess();

        // Grace period: if the process survives a couple of seconds without a fatal
        // error, treat it as connected. sing-box exits fast on config errors.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1800);
            lock (_gate)
            {
                if (_wantRunning && _process is { HasExited: false } && Status == ConnectionStatus.Connecting)
                    SetStatus(ConnectionStatus.Connected, $"Подключено · {_server?.Name}");
            }
        });
    }

    public Task StopAsync()
    {
        _wantRunning = false;
        Process? proc;
        lock (_gate)
        {
            proc = _process;
            _process = null;
        }

        if (proc is not null)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
                proc.WaitForExit(4000);
            }
            catch { /* already gone */ }
            finally { proc.Dispose(); }
        }

        SetStatus(ConnectionStatus.Disconnected, "Отключено");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Kill any orphaned sing-box processes from a previous run (e.g. after a crash)
    /// so they don't keep a stale TUN up or hold the Clash-API port, which would make
    /// the new instance fail and break connectivity.
    /// </summary>
    private static void KillStrayCores()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("sing-box"))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path is null ||
                        string.Equals(path, Paths.SingBoxExe, StringComparison.OrdinalIgnoreCase))
                        p.Kill(entireProcessTree: true);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
    }

    private void LaunchProcess()
    {
        var config = SingBoxConfigBuilder.Build(_server!, _apps, _settings);
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
        proc.Exited += OnProcessExited;

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        lock (_gate) _process = proc;
    }

    private void OnOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        LogLine?.Invoke(this, line);
        try { File.AppendAllText(Paths.LogFile, line + Environment.NewLine); } catch { }

        // Surface obvious fatal errors immediately.
        if (line.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("configuration error", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(ConnectionStatus.Error, line);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (!_wantRunning)
            return; // expected stop

        // Unexpected exit. Kill-switch: while the core is down, the TUN is gone and
        // tunneled apps would use the direct route — so restart quickly.
        if (_settings.KillSwitch && _restartAttempts < MaxRestartAttempts)
        {
            _restartAttempts++;
            SetStatus(ConnectionStatus.Reconnecting, $"Ядро перезапускается ({_restartAttempts}/{MaxRestartAttempts})…");
            try { LaunchProcess(); }
            catch (Exception ex) { SetStatus(ConnectionStatus.Error, ex.Message); }
        }
        else
        {
            _wantRunning = false;
            SetStatus(ConnectionStatus.Error, "Ядро неожиданно остановилось.");
        }
    }

    /// <summary>Hot-reload: rebuild config and restart the core to apply changes.</summary>
    public async Task ApplyAsync(ServerConfig server, IReadOnlyList<TunneledApp> apps, AppSettings settings)
    {
        if (IsRunning || _wantRunning)
            await StartAsync(server, apps, settings);
    }

    private void SetStatus(ConnectionStatus status, string? message)
    {
        Status = status;
        StatusChanged?.Invoke(this, new CoreStatusEventArgs { Status = status, Message = message });
    }
}
