using System.Windows;
using System.Windows.Input;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Views;

public partial class FlyoutWindow : Window
{
    public FlyoutWindow()
    {
        InitializeComponent();
        Deactivated += OnDeactivated;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Don't hide while a native OS dialog (file picker) is in front.
        if (FileDialogService.DialogOpen) return;
        // Test hook: keep the window visible for screenshots/verification.
        if (Environment.GetEnvironmentVariable("TUNNELDECK_NOHIDE") == "1") return;
        Hide();
        if (DataContext is MainViewModel vm)
            vm.CurrentPage = Page.Main;
    }

    /// <summary>Anchor the flyout to the bottom-right, just above the tray.</summary>
    public void PositionNearTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 4;
        Top = area.Bottom - Height - 4;
    }

    private void RunningList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.AddSelectedRunningCommand.CanExecute(null))
            vm.AddSelectedRunningCommand.Execute(null);
    }
}
