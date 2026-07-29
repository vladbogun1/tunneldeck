using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TunnelDeck.Services;

public sealed record UpdateInfo(Version Version, string Tag, string InstallerUrl, string Notes, string HtmlUrl);

/// <summary>
/// Checks GitHub Releases for a newer version and downloads the installer.
/// The installer replaces the running app (it kills the process on install).
/// </summary>
public sealed class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/vladbogun1/tunneldeck/releases/latest";
    private static readonly Regex InstallerName = new(@"TunnelDeck-Setup-.*\.exe$", RegexOptions.IgnoreCase);

    public static Version CurrentVersion
    {
        get
        {
            // Test override so the update banner can be demoed without a newer release.
            var env = Environment.GetEnvironmentVariable("TUNNELDECK_UPDATE_BASE");
            if (!string.IsNullOrWhiteSpace(env) && Version.TryParse(env, out var ov)) return Norm(ov);
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return Norm(v);
        }
    }

    private static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TunnelDeck-Updater");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        var json = await http.GetStringAsync(LatestApi, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var htmlUrl = root.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(htmlUrl))
            htmlUrl = $"https://github.com/vladbogun1/tunneldeck/releases/tag/{tag}";
        if (!TryParseTag(tag, out var version)) return null;
        if (version <= CurrentVersion) return null;

        string? url = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (InstallerName.IsMatch(name) &&
                    a.TryGetProperty("browser_download_url", out var d))
                {
                    url = d.GetString();
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(url)) return null;

        return new UpdateInfo(version, tag, url, notes, htmlUrl);
    }

    private static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var s = (tag ?? "").TrimStart('v', 'V').Trim();
        if (Version.TryParse(s, out var v)) { version = Norm(v); return true; }
        return false;
    }

    /// <summary>Download the installer to a temp file; returns its path.</summary>
    public async Task<string> DownloadInstallerAsync(UpdateInfo info, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"TunnelDeck-Setup-{info.Version}.exe");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TunnelDeck-Updater");
        using var resp = await http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((int)(read * 100 / total));
        }
        return path;
    }
}
