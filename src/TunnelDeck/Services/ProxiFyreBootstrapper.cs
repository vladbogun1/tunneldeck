using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Microsoft.Win32;

namespace TunnelDeck.Services;

/// <summary>
/// Locates (and if needed downloads) the ProxiFyre binaries, and reports whether the
/// Windows Packet Filter driver is installed.
///
/// ProxiFyre location: prefer a "proxifyre" folder next to the app (placed by the
/// installer); otherwise fall back to %LOCALAPPDATA%\TunnelDeck\proxifyre and download.
/// </summary>
public static class ProxiFyreBootstrapper
{
    public const string Version = "2.4.0";
    private static string ZipUrl =>
        $"https://github.com/wiresock/proxifyre/releases/download/v{Version}/ProxiFyre-v{Version}-x64-signed.zip";

    public static string Dir { get; }
    public static string Exe => Path.Combine(Dir, "ProxiFyre.exe");
    public static string ConfigPath => Path.Combine(Dir, "app-config.json");

    static ProxiFyreBootstrapper()
    {
        var appDir = Path.Combine(AppContext.BaseDirectory, "proxifyre");
        Dir = File.Exists(Path.Combine(appDir, "ProxiFyre.exe"))
            ? appDir
            : Path.Combine(Paths.DataDir, "proxifyre");
    }

    public static bool IsInstalled => File.Exists(Exe);

    /// <summary>The Windows Packet Filter driver registers a service named "ndisrd".</summary>
    public static bool IsDriverInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ndisrd");
            return key is not null;
        }
    }

    public static async Task EnsureAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (IsInstalled) return;

        Directory.CreateDirectory(Dir);
        progress?.Report($"Загрузка ProxiFyre {Version}…");

        var zip = Path.Combine(Dir, "proxifyre.zip");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TunnelDeck");
            using var resp = await http.GetAsync(ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zip);
            await src.CopyToAsync(dst, ct);
        }

        progress?.Report("Распаковка ProxiFyre…");
        ZipFile.ExtractToDirectory(zip, Dir, overwriteFiles: true);
        try { File.Delete(zip); } catch { }

        if (!IsInstalled)
            throw new InvalidOperationException("ProxiFyre.exe не найден после распаковки.");
    }
}
