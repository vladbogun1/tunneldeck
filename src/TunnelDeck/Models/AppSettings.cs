namespace TunnelDeck.Models;

public sealed class AppSettings
{
    /// <summary>Start TunnelDeck with Windows (registry Run key).</summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>Reconnect the VPN automatically when the app starts.</summary>
    public bool AutoConnectOnLaunch { get; set; } = false;

    /// <summary>
    /// Kill-switch: if the core stops unexpectedly, block the tunneled apps'
    /// traffic instead of letting it fall back to the direct connection.
    /// (Implemented as a route rule so no packets leak while reconnecting.)
    /// </summary>
    public bool KillSwitch { get; set; } = true;

    /// <summary>Route DNS queries of tunneled apps through the proxy (anti-leak).</summary>
    public bool ProxyDnsForTunneledApps { get; set; } = true;

    /// <summary>sing-box log verbosity: trace|debug|info|warn|error</summary>
    public string LogLevel { get; set; } = "warn";

    /// <summary>UI language: "system" (follow Windows) | "ru" | "en".</summary>
    public string Language { get; set; } = "system";
}
