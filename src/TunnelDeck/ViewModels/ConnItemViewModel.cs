using TunnelDeck.Services;

namespace TunnelDeck.ViewModels;

/// <summary>A single active connection row (destination host + traffic).</summary>
public sealed class ConnItemViewModel
{
    public string Host { get; }
    public string Detail { get; }
    public string TrafficText { get; }

    public ConnItemViewModel(TrafficStatsService.ConnectionInfo c)
    {
        Host = c.Host;
        Detail = c.Detail;
        TrafficText = $"↓ {TrafficStatsService.FormatBytes(c.Down)}  ↑ {TrafficStatsService.FormatBytes(c.Up)}";
    }
}
