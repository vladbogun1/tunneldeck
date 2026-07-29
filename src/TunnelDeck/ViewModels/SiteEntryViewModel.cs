using CommunityToolkit.Mvvm.ComponentModel;

namespace TunnelDeck.ViewModels;

/// <summary>A website (domain) routed through the VPN, shown on the main page.</summary>
public sealed partial class SiteEntryViewModel : ObservableObject
{
    public string Domain { get; }

    public SiteEntryViewModel(string domain) => Domain = domain;

    [ObservableProperty] private bool _showSpeed;
    [ObservableProperty] private string _downText = "0 Б/с";
    [ObservableProperty] private string _upText = "0 Б/с";
}
