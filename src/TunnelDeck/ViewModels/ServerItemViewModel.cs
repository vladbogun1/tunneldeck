using CommunityToolkit.Mvvm.ComponentModel;
using TunnelDeck.Models;

namespace TunnelDeck.ViewModels;

/// <summary>A server row in the dropdown, with a live ping badge.</summary>
public sealed partial class ServerItemViewModel : ObservableObject
{
    public ServerConfig Config { get; }
    public string Name { get; }

    public ServerItemViewModel(ServerConfig config)
    {
        Config = config;
        Name = config.Name;
    }

    /// <summary>e.g. "42 мс", "—", or "" (unknown).</summary>
    [ObservableProperty] private string _pingText = "";
}
