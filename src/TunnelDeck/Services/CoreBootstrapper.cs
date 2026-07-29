using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace TunnelDeck.Services;

/// <summary>
/// Ensures the sing-box core binary is present. On first run it downloads the
/// pinned release from GitHub and extracts sing-box.exe into the core dir.
///
/// The Windows build of sing-box embeds wintun and extracts it at runtime, so no
/// separate wintun.dll needs to be shipped alongside the executable.
/// </summary>
public sealed class CoreBootstrapper
{
    // Pinned to the last 1.11.x line: its config schema is what
    // SingBoxConfigBuilder targets. Bump deliberately, not automatically.
    public const string Version = "1.11.15";

    private static string ZipName => $"sing-box-{Version}-windows-amd64.zip";
    private static string DownloadUrl =>
        $"https://github.com/SagerNet/sing-box/releases/download/v{Version}/{ZipName}";

    public bool IsInstalled => File.Exists(Paths.SingBoxExe);

    public async Task EnsureAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (IsInstalled) return;

        Paths.EnsureDirs();
        progress?.Report($"Downloading sing-box {Version}…");

        var zipPath = Path.Combine(Paths.CoreDir, ZipName);
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TunnelDeck");
            using var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zipPath);
            await src.CopyToAsync(dst, ct);
        }

        progress?.Report("Extracting core…");
        ExtractSingBox(zipPath);

        try { File.Delete(zipPath); } catch { /* best effort */ }

        if (!IsInstalled)
            throw new InvalidOperationException("sing-box.exe was not found after extraction.");

        progress?.Report("Core ready.");
    }

    private static void ExtractSingBox(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals("sing-box.exe", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new InvalidOperationException("sing-box.exe not present in the downloaded archive.");

        entry.ExtractToFile(Paths.SingBoxExe, overwrite: true);
    }
}
