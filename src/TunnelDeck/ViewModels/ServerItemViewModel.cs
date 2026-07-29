using CommunityToolkit.Mvvm.ComponentModel;
using TunnelDeck.Models;
using TunnelDeck.Services;

namespace TunnelDeck.ViewModels;

/// <summary>A server row in the dropdown, with a country flag and a live colored ping badge.</summary>
public sealed partial class ServerItemViewModel : ObservableObject
{
    public ServerConfig Config { get; }
    public string Name { get; }

    /// <summary>Country flag for this server (null if the country can't be inferred).</summary>
    public System.Windows.Media.ImageSource? FlagImage { get; }
    public bool HasFlag => FlagImage is not null;
    public string CountryCode { get; }

    public ServerItemViewModel(ServerConfig config)
    {
        Config = config;
        Name = config.Name;
        CountryCode = CountryCodes.FromName(config.Name);
        FlagImage = FlagFactory.For(CountryCode);
    }

    // -2 unknown (blank), -3 measuring (…), -1 failed (—), >=0 latency in ms.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingText))]
    [NotifyPropertyChangedFor(nameof(PingBrush))]
    [NotifyPropertyChangedFor(nameof(HasPing))]
    private int _pingMs = -2;

    public bool HasPing => PingMs != -2;

    /// <summary>e.g. "42 мс", "…", "—", or "" (unknown).</summary>
    public string PingText => PingMs switch
    {
        -3 => "…",
        -2 => "",
        -1 => "—",
        _ => $"{PingMs} мс"
    };

    /// <summary>Green &lt;100ms, amber &lt;250ms, red higher; grey while measuring/failed.</summary>
    public System.Windows.Media.Brush PingBrush => new System.Windows.Media.SolidColorBrush(
        PingMs switch
        {
            >= 0 and < 100 => System.Windows.Media.Color.FromRgb(0x0F, 0x7B, 0x0F),
            >= 100 and < 250 => System.Windows.Media.Color.FromRgb(0xB0, 0x71, 0x05),
            >= 250 => System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C),
            _ => System.Windows.Media.Color.FromRgb(0x9A, 0x9A, 0x9A)
        });
}
