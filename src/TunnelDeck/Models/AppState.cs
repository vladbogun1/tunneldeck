namespace TunnelDeck.Models;

/// <summary>Everything TunnelDeck persists between runs.</summary>
public sealed class AppState
{
    /// <summary>The raw subscription URL the user pasted.</summary>
    public string SubscriptionUrl { get; set; } = "";

    /// <summary>
    /// Stable per-install device id. Some panels (Remnawave/Happ-locked) only
    /// return the real config when a client sends a Happ UA plus an x-hwid header.
    /// Generated once and reused so we occupy a single device slot.
    /// </summary>
    public string Hwid { get; set; } = "";

    /// <summary>Servers parsed from the subscription on the last refresh.</summary>
    public List<ServerConfig> Servers { get; set; } = new();

    /// <summary>Id of the currently selected server.</summary>
    public string? SelectedServerId { get; set; }

    /// <summary>Apps the user wants routed through the VPN.</summary>
    public List<TunneledApp> TunneledApps { get; set; } = new();

    public AppSettings Settings { get; set; } = new();

    public ServerConfig? SelectedServer =>
        Servers.FirstOrDefault(s => s.Id == SelectedServerId) ?? Servers.FirstOrDefault();
}
