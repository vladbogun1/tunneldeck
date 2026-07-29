using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Fetches a subscription URL and turns it into a list of servers.
///
/// Different providers gate content differently:
///  - Remnawave/Happ-locked panels return the real config only for a
///    "Happ/&lt;version&gt;" User-Agent PLUS an x-hwid header (otherwise a decoy).
///  - Classic panels return a base64 vless list for common client UAs.
/// So we try several strategies and keep the first that yields real servers.
///
/// Every attempt is logged to subscription.log so failures can be diagnosed.
/// Response bodies can be: Xray/V2Ray JSON, base64 vless list, or plaintext.
/// </summary>
public sealed class SubscriptionService
{
    private readonly record struct Strategy(string Name, string UserAgent, bool SendHwid);

    private static readonly Strategy[] Strategies =
    {
        new("happ",     "Happ/1.11.0",     true),
        new("v2rayng",  "v2rayNG/1.8.19",  false),
        new("clash",    "clash-verge/1.7.0", false),
        new("singbox",  "sing-box/1.11.0", false),
        new("browser",  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36", false),
    };

    public async Task<IReadOnlyList<ServerConfig>> FetchAsync(
        string subscriptionUrl, string hwid, CancellationToken ct = default)
    {
        Log($"===== fetch {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        Log($"url: {subscriptionUrl}");
        Log($"hwid: {hwid}");

        if (string.IsNullOrWhiteSpace(subscriptionUrl))
            throw new ArgumentException("Ссылка подписки пуста.");

        if (subscriptionUrl.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            var direct = VlessParser.ParseMany(subscriptionUrl);
            Log($"direct vless uri -> {direct.Count} server(s)");
            if (direct.Count > 0) return direct;
            throw new InvalidOperationException("Не удалось разобрать vless-ссылку.");
        }

        IReadOnlyList<ServerConfig> best = Array.Empty<ServerConfig>();
        string? lastError = null;

        foreach (var s in Strategies)
        {
            try
            {
                var (status, contentType, body) = await DownloadAsync(subscriptionUrl, s.UserAgent, s.SendHwid ? hwid : null, ct);
                var servers = Decode(body);
                Log($"[{s.Name}] ua='{s.UserAgent}' hwid={(s.SendHwid ? "yes" : "no")} " +
                    $"-> HTTP {status}, {body.Length} bytes, type={contentType}, servers={servers.Count} " +
                    $":: {Snippet(body)}");

                // If the server answered with JSON but we got 0 real servers, it's a
                // decoy — log the provider's message (device limit / expired / Happ-only).
                if (servers.Count == 0 && XrayJsonParser.LooksLikeXrayJson(body.Trim()))
                    Log($"    decoy reason: {XrayJsonParser.ExtractDiagnostics(body.Trim())}");

                if (servers.Count > best.Count) best = servers;
                if (servers.Count > 0)
                {
                    Log($"OK — using strategy '{s.Name}' with {servers.Count} server(s): " +
                        string.Join(", ", servers.Select(x => x.Name)));
                    return servers;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                Log($"[{s.Name}] ua='{s.UserAgent}' ERROR: {ex.Message}");
            }
        }

        if (best.Count > 0) return best;

        Log("RESULT: no usable servers from any strategy.");
        throw new InvalidOperationException(
            "Сервер ответил, но рабочих серверов не найдено. Возможно, провайдер " +
            "привязал ключ к клиенту Happ, либо ключ истёк. Подробности — в файле " +
            "subscription.log (папка %LOCALAPPDATA%\\TunnelDeck)." +
            (lastError is null ? "" : $" Последняя ошибка: {lastError}"));
    }

    private static async Task<(int status, string? contentType, string body)> DownloadAsync(
        string url, string ua, string? hwid, CancellationToken ct)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", ua);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        if (!string.IsNullOrWhiteSpace(hwid))
        {
            req.Headers.TryAddWithoutValidation("x-hwid", hwid);
            req.Headers.TryAddWithoutValidation("x-device-os", "Windows");
            req.Headers.TryAddWithoutValidation("x-ver-os", "11");
            req.Headers.TryAddWithoutValidation("x-device-model", "TunnelDeck");
        }

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return ((int)resp.StatusCode, resp.Content.Headers.ContentType?.ToString(), body);
    }

    private static List<ServerConfig> Decode(string body)
    {
        body = body.Trim();
        if (body.Length == 0) return new();

        if (XrayJsonParser.LooksLikeXrayJson(body))
        {
            try { return XrayJsonParser.ParseMany(body).ToList(); }
            catch { /* fall through */ }
        }

        var decoded = TryBase64Decode(body);
        if (decoded is not null && decoded.Contains("://"))
            return VlessParser.ParseMany(decoded).ToList();

        return VlessParser.ParseMany(body).ToList();
    }

    private static string? TryBase64Decode(string s)
    {
        try
        {
            var t = s.Replace('-', '+').Replace('_', '/').Trim();
            switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; }
            return Encoding.UTF8.GetString(Convert.FromBase64String(t));
        }
        catch { return null; }
    }

    /// <summary>First ~160 chars of the body, with UUIDs redacted, for the log.</summary>
    private static string Snippet(string body)
    {
        var s = body.Length > 160 ? body[..160] : body;
        s = Regex.Replace(s, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "<uuid>");
        return s.Replace("\r", " ").Replace("\n", " ");
    }

    private static void Log(string line)
    {
        try
        {
            Paths.EnsureDirs();
            File.AppendAllText(Paths.SubscriptionLog, line + Environment.NewLine);
        }
        catch { }
    }
}
