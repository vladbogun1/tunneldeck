using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TunnelDeck.Models;
using TunnelDeck.Services;

namespace TunnelDeck.ViewModels;

/// <summary>A single row in the tunneled-apps list.</summary>
public sealed partial class AppEntryViewModel : ObservableObject
{
    public TunneledApp Model { get; }

    public AppEntryViewModel(TunneledApp model)
    {
        Model = model;
        _enabled = model.Enabled;
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Model.DisplayName)
        ? Model.ProcessName
        : Model.DisplayName;

    public string ProcessName => Model.ProcessName;

    public ImageSource? Icon => IconExtractor.GetIcon(Model.ExecutablePath);

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value)
    {
        Model.Enabled = value;
        EnabledChanged?.Invoke(this, value);
    }

    /// <summary>Raised when the per-app toggle flips, so the owner can persist + hot-reload.</summary>
    public event EventHandler<bool>? EnabledChanged;

    // ---- Live traffic (shown only while connected) ----
    [ObservableProperty] private bool _showSpeed;
    [ObservableProperty] private string _downText = "0 Б/с";
    [ObservableProperty] private string _upText = "0 Б/с";

    public void SetSpeed(long upBps, long downBps)
    {
        UpText = Services.TrafficStatsService.Format(upBps);
        DownText = Services.TrafficStatsService.Format(downBps);
    }
}
