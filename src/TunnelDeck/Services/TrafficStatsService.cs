using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace TunnelDeck.Services;

/// <summary>
/// Polls the sing-box Clash API and reports the total tunnel throughput
/// (bytes/sec, up and down). Per-process attribution isn't possible in proxy mode
/// (sing-box sees connections from ProxiFyre, not the real app), so we report the
/// aggregate from the cumulative downloadTotal/uploadTotal counters.
/// </summary>
public sealed class TrafficStatsService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private CancellationTokenSource? _cts;

    private long _prevUp, _prevDown, _lastTicks;
    private long _sessUp, _sessDown;   // cumulative for this session (robust to core restarts)
    private bool _hasPrev;

    /// <summary>(upBps, downBps, sessUpTotal, sessDownTotal) — per-sec speed + cumulative bytes.</summary>
    public event EventHandler<(long up, long down, long sessUp, long sessDown)>? Updated;

    public void Start()
    {
        Stop();
        _hasPrev = false;
        _sessUp = _sessDown = 0;
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        Updated?.Invoke(this, (0, 0, _sessUp, _sessDown));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var url = SingBoxConfigBuilder.ClashApiBaseUrl + "/connections";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await _http.GetStringAsync(url, ct);
                var (up, down) = ComputeSpeed(json);
                Updated?.Invoke(this, (up, down, _sessUp, _sessDown));
            }
            catch
            {
                // API not up yet / transient — report nothing this tick.
            }
            try { await Task.Delay(1000, ct); } catch { break; }
        }
    }

    private (long up, long down) ComputeSpeed(string json)
    {
        long up = 0, down = 0;
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("uploadTotal", out var u) && u.TryGetInt64(out var uu)) up = uu;
            if (root.TryGetProperty("downloadTotal", out var d) && d.TryGetInt64(out var dd)) down = dd;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = _hasPrev ? (now - _lastTicks) / (double)Stopwatch.Frequency : 1.0;
        if (elapsed <= 0.05) elapsed = 1.0;

        long upBps = 0, downBps = 0;
        if (_hasPrev)
        {
            // Guard against counter resets (core restart).
            var du = up >= _prevUp ? up - _prevUp : 0;
            var dd = down >= _prevDown ? down - _prevDown : 0;
            upBps = (long)(du / elapsed);
            downBps = (long)(dd / elapsed);
            _sessUp += du;
            _sessDown += dd;
        }

        _prevUp = up; _prevDown = down; _lastTicks = now; _hasPrev = true;
        return (upBps, downBps);
    }

    public static string Format(long bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec} Б/с";
        double kb = bytesPerSec / 1024.0;
        if (kb < 1024) return $"{kb:0} КБ/с";
        double mb = kb / 1024.0;
        return $"{mb:0.0} МБ/с";
    }

    /// <summary>Format a cumulative byte count (e.g. "1,4 ГБ").</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} Б";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0} КБ";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.0} МБ";
        double gb = mb / 1024.0;
        return $"{gb:0.00} ГБ";
    }
}
