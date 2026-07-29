namespace TunnelDeck.Models;

/// <summary>
/// One application the user wants routed through the VPN.
/// Matching is done by process image name (e.g. "chrome.exe"); the full
/// path is kept for display, icon extraction and disambiguation.
/// </summary>
public sealed class TunneledApp
{
    /// <summary>Image file name, lowercased, e.g. "discord.exe". This is what sing-box matches.</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>Full path to the executable, if known. May be empty for a name-only rule.</summary>
    public string ExecutablePath { get; set; } = "";

    /// <summary>Friendly label shown in the UI (product name or file name).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Whether this app is currently active in the routing rules.</summary>
    public bool Enabled { get; set; } = true;

    public static string NormalizeName(string fileName)
    {
        var name = System.IO.Path.GetFileName(fileName).Trim().ToLowerInvariant();
        if (!name.EndsWith(".exe") && name.Length > 0)
            name += ".exe";
        return name;
    }
}
