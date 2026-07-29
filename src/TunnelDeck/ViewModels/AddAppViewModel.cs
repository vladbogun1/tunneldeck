using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelDeck.Models;
using TunnelDeck.Services;

namespace TunnelDeck.ViewModels;

public sealed partial class RunningAppItem : ObservableObject
{
    public required string DisplayName { get; init; }
    public required string ProcessName { get; init; }
    public required string ExecutablePath { get; init; }
    public ImageSource? Icon => IconExtractor.GetIcon(ExecutablePath);
}

public sealed partial class AddAppViewModel : ObservableObject
{
    private List<RunningAppItem> _all = new();

    public ObservableCollection<RunningAppItem> Apps { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private RunningAppItem? _selected;
    [ObservableProperty] private bool _isLoading;

    public AddAppViewModel()
    {
        _ = LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var list = await Task.Run(() => ProcessService.GetRunningApps()
                .Select(p => new RunningAppItem
                {
                    DisplayName = p.DisplayName,
                    ProcessName = p.ProcessName,
                    ExecutablePath = p.ExecutablePath
                })
                .ToList());

            _all = list;
            ApplyFilter();
        }
        finally { IsLoading = false; }
    }

    partial void OnSearchChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = Search?.Trim() ?? "";
        Apps.Clear();
        foreach (var item in _all)
        {
            if (q.Length == 0 ||
                item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                Apps.Add(item);
            }
        }
    }

    public static TunneledApp FromRunning(RunningAppItem item) => new()
    {
        ProcessName = item.ProcessName,
        ExecutablePath = item.ExecutablePath,
        DisplayName = item.DisplayName,
        Enabled = true
    };

    public static TunneledApp FromExePath(string exePath) => new()
    {
        ProcessName = TunneledApp.NormalizeName(exePath),
        ExecutablePath = exePath,
        DisplayName = ProcessService.GetDisplayName(exePath, System.IO.Path.GetFileNameWithoutExtension(exePath)),
        Enabled = true
    };
}
