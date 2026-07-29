using System.Net;
using System.Net.Http;

namespace TunnelDeck.Services;

public enum LeakStatus { Ok, Leaking, TunnelDown }

/// <summary>
/// While connected, periodically checks that the tunnel is actually carrying
/// traffic: it queries the exit IP through the SOCKS proxy and compares it to the
/// real (direct) IP. If the proxy query fails the tunnel is down; if the proxy exit
/// equals the direct IP, tunneled apps aren't really being tunneled (leak).
/// </summary>
public sealed class LeakMonitor
{
    private CancellationTokenSource? _cts;

    /// <summary>Raised on the thread pool with the latest status.</summary>
    public event EventHandler<LeakStatus>? StatusChanged;

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // Give the tunnel a moment to come up before the first probe.
        try { await Task.Delay(5000, ct); } catch { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var proxyIp = await ExitIpAsync(viaProxy: true, ct);
                if (proxyIp is null)
                {
                    Raise(LeakStatus.TunnelDown);
                }
                else
                {
                    var directIp = await ExitIpAsync(viaProxy: false, ct);
                    Raise(directIp is not null && directIp == proxyIp ? LeakStatus.Leaking : LeakStatus.Ok);
                }
            }
            catch { /* transient — try again next tick */ }

            try { await Task.Delay(45_000, ct); } catch { break; }
        }
    }

    private void Raise(LeakStatus s)
    {
        try { StatusChanged?.Invoke(this, s); } catch { }
    }

    private static async Task<string?> ExitIpAsync(bool viaProxy, CancellationToken ct)
    {
        try
        {
            var handler = new HttpClientHandler { UseProxy = viaProxy };
            if (viaProxy) handler.Proxy = new WebProxy($"socks5://{SingBoxConfigBuilder.SocksEndpoint}");
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "curl/8");
            var trace = await http.GetStringAsync("https://www.cloudflare.com/cdn-cgi/trace", ct);
            foreach (var line in trace.Split('\n'))
                if (line.StartsWith("ip=", StringComparison.OrdinalIgnoreCase))
                    return line[3..].Trim();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
