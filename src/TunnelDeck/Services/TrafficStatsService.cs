using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace TunnelDeck.Services;

public readonly record struct ProcessSpeed(long UploadBps, long DownloadBps);

/// <summary>
/// Polls the sing-box Clash API (<c>/connections</c>) once per second and reports
/// per-process upload/download speed (bytes per second), keyed by process image
/// name (e.g. "chrome.exe"). Speed is computed from the per-connection cumulative
/// byte counters between polls; brand-new connections are seeded (counted from
/// their first sighting) to avoid spikes.
/// </summary>
public sealed class TrafficStatsService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private CancellationTokenSource? _cts;

    // connection id -> (upload, download) cumulative bytes at last poll
    private Dictionary<string, (long up, long down)> _prev = new();
    private long _lastTicks;

    public event EventHandler<IReadOnlyDictionary<string, ProcessSpeed>>? Updated;

    public void Start()
    {
        Stop();
        _prev = new Dictionary<string, (long, long)>();
        _lastTicks = Stopwatch.GetTimestamp();
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
        var url = SingBoxConfigBuilder.ClashApiBaseUrl + "/connections";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await _http.GetStringAsync(url, ct);
                var speeds = Compute(json);
                Updated?.Invoke(this, speeds);
            }
            catch
            {
                // API not up yet / transient — ignore and retry.
            }

            try { await Task.Delay(1000, ct); } catch { break; }
        }
    }

    private IReadOnlyDictionary<string, ProcessSpeed> Compute(string json)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastTicks) / (double)Stopwatch.Frequency;
        if (elapsed <= 0.05) elapsed = 1.0;
        _lastTicks = now;

        var current = new Dictionary<string, (long up, long down)>();
        var upByProc = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var downByProc = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("connections", out var conns) &&
            conns.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in conns.EnumerateArray())
            {
                var id = GetString(c, "id");
                if (id.Length == 0) continue;

                long up = GetLong(c, "upload");
                long down = GetLong(c, "download");
                current[id] = (up, down);

                string proc = "";
                if (c.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                    proc = GetString(meta, "process");
                if (proc.Length == 0) continue;

                // Delta since last poll (0 for a connection we've not seen before).
                long dUp = 0, dDown = 0;
                if (_prev.TryGetValue(id, out var p))
                {
                    dUp = Math.Max(0, up - p.up);
                    dDown = Math.Max(0, down - p.down);
                }

                upByProc[proc] = upByProc.GetValueOrDefault(proc) + dUp;
                downByProc[proc] = downByProc.GetValueOrDefault(proc) + dDown;
            }
        }

        _prev = current;

        var result = new Dictionary<string, ProcessSpeed>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in upByProc.Keys.Union(downByProc.Keys))
        {
            var up = (long)(upByProc.GetValueOrDefault(proc) / elapsed);
            var down = (long)(downByProc.GetValueOrDefault(proc) / elapsed);
            result[proc] = new ProcessSpeed(up, down);
        }
        return result;
    }

    private static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long GetLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    public static string Format(long bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec} Б/с";
        double kb = bytesPerSec / 1024.0;
        if (kb < 1024) return $"{kb:0} КБ/с";
        double mb = kb / 1024.0;
        return $"{mb:0.0} МБ/с";
    }
}
