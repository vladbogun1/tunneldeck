using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Views;

public partial class FlyoutWindow : Window
{
    public FlyoutWindow()
    {
        InitializeComponent();
        // The window no longer auto-hides on focus loss, so you can watch it while
        // browsing. It hides only via the minimize button or the tray icon.
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) FadeIn(); };
    }

    /// <summary>Anchor the flyout to the bottom-right, just above the tray.</summary>
    public void PositionNearTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 4;
        Top = area.Bottom - Height - 4;
    }

    private void FadeIn()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(150))));
    }

    /// <summary>Hide to tray (via the minimize button) and reset to the main page.</summary>
    public void MinimizeToTray()
    {
        Hide();
        if (DataContext is MainViewModel vm) vm.CurrentPage = Page.Main;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => MinimizeToTray();

    private void RunningList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.AddSelectedRunningCommand.CanExecute(null))
            vm.AddSelectedRunningCommand.Execute(null);
    }

    private void ServerBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.MeasurePings();
    }
}
