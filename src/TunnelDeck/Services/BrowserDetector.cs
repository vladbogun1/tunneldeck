using System.IO;
using Microsoft.Win32;

namespace TunnelDeck.Services;

/// <summary>
/// Detects installed web browsers so "site mode" can redirect all of them.
///
/// Primary source: the Windows registry "StartMenuInternet" clients list (the same
/// list Windows uses for the default-browser picker) — every properly installed
/// browser registers there. We union that with a fallback list of well-known names
/// so mainstream browsers are covered even if a registration is missing. (Portable /
/// unregistered browsers can't be auto-detected and are not covered.)
/// </summary>
public static class BrowserDetector
{
    private static readonly string[] Fallback =
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe",
        "opera.exe", "opera_gx.exe", "vivaldi.exe", "browser.exe" // browser.exe = Yandex
    };

    /// <summary>Distinct browser executable names (lowercased, e.g. "chrome.exe").</summary>
    public static IReadOnlyList<string> GetBrowserExeNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectFrom(Registry.LocalMachine, @"SOFTWARE\Clients\StartMenuInternet", names);
        CollectFrom(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet", names);
        CollectFrom(Registry.CurrentUser, @"SOFTWARE\Clients\StartMenuInternet", names);

        foreach (var f in Fallback) names.Add(f);
        return names.ToList();
    }

    private static void CollectFrom(RegistryKey root, string path, HashSet<string> into)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null) return;
            foreach (var browser in key.GetSubKeyNames())
            {
                try
                {
                    using var cmd = key.OpenSubKey($@"{browser}\shell\open\command");
                    var raw = cmd?.GetValue(null) as string;
                    var exe = ExtractExe(raw);
                    if (exe is not null) into.Add(exe);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Pull the .exe file name out of a shell command string.</summary>
    private static string? ExtractExe(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var s = command.Trim();
        string path;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            path = end > 0 ? s[1..end] : s.Trim('"');
        }
        else
        {
            var sp = s.IndexOf(' ');
            path = sp > 0 ? s[..sp] : s;
        }
        try
        {
            var name = Path.GetFileName(path).Trim().ToLowerInvariant();
            return name.EndsWith(".exe") ? name : null;
        }
        catch { return null; }
    }
}
