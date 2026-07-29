using System.Diagnostics;
using System.IO;

namespace TunnelDeck.Services;

public sealed record RunningProcessInfo(string DisplayName, string ProcessName, string ExecutablePath);

/// <summary>Enumerates running user applications so the user can pick which to tunnel.</summary>
public static class ProcessService
{
    private static readonly string SelfPath =
        Process.GetCurrentProcess().MainModule?.FileName ?? "";

    private static readonly string WindowsDir =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>
    /// Returns distinct running executables that have a real on-disk path,
    /// filtered to plausible user apps (excludes Windows system folder and self).
    /// </summary>
    public static List<RunningProcessInfo> GetRunningApps(bool includeSystem = false)
    {
        var seen = new Dictionary<string, RunningProcessInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                if (string.Equals(path, SelfPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (!includeSystem && path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase)) continue;
                if (seen.ContainsKey(path)) continue;

                seen[path] = new RunningProcessInfo(GetDisplayName(path, p.ProcessName), Path.GetFileName(path).ToLowerInvariant(), path);
            }
            catch
            {
                // Protected / exited process — skip.
            }
            finally
            {
                p.Dispose();
            }
        }

        return seen.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string GetDisplayName(string exePath, string fallback)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var name = info.ProductName;
            if (string.IsNullOrWhiteSpace(name)) name = info.FileDescription;
            if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();
        }
        catch { }
        return string.IsNullOrWhiteSpace(fallback) ? Path.GetFileNameWithoutExtension(exePath) : fallback;
    }
}
