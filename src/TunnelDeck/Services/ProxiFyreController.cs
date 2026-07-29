using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Manages the ProxiFyre process, which transparently redirects the selected
/// applications' traffic into the local sing-box SOCKS proxy (per-process, via the
/// Windows Packet Filter driver — no system routing changes).
/// </summary>
public sealed class ProxiFyreController
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private Process? _process;

    public bool IsRunning
    {
        get { lock (_gate) return _process is { HasExited: false }; }
    }

    /// <summary>Write ProxiFyre's app-config.json from the enabled tunneled apps.</summary>
    public void WriteConfig(IReadOnlyList<TunneledApp> apps)
    {
        var appNames = apps
            .Where(a => a.Enabled)
            .Select(a => string.IsNullOrWhiteSpace(a.ExecutablePath) ? a.ProcessName : a.ExecutablePath)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var config = new Dictionary<string, object?>
        {
            ["logLevel"] = "Error",
            ["bypassLan"] = false,
            ["proxies"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["appNames"] = appNames,
                    ["socks5ProxyEndpoint"] = SingBoxConfigBuilder.SocksEndpoint,
                    ["supportedProtocols"] = new[] { "TCP", "UDP" },
                    ["supportedAddressFamilies"] = new[] { "IPv4", "IPv6" }
                }
            },
            ["excludes"] = Array.Empty<string>()
        };

        Directory.CreateDirectory(ProxiFyreBootstrapper.Dir);
        File.WriteAllText(ProxiFyreBootstrapper.ConfigPath, JsonSerializer.Serialize(config, Json));
    }

    public void Start()
    {
        Stop();
        var psi = new ProcessStartInfo
        {
            FileName = ProxiFyreBootstrapper.Exe,
            WorkingDirectory = ProxiFyreBootstrapper.Dir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        lock (_gate) _process = Process.Start(psi);
    }

    public void Stop()
    {
        Process? proc;
        lock (_gate) { proc = _process; _process = null; }
        if (proc is not null)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); proc.WaitForExit(3000); }
            catch { }
            finally { proc.Dispose(); }
        }
        KillStray();
    }

    /// <summary>Kill any orphaned ProxiFyre from a previous run.</summary>
    public static void KillStray()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("ProxiFyre"))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
    }
}
