using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Views;

public partial class FlyoutWindow : Window
{
    private bool _fadingOut;

    public FlyoutWindow()
    {
        InitializeComponent();
        // The window no longer auto-hides on focus loss, so you can watch it while
        // browsing. It hides only via the ✕ button or the tray icon — both fade out.
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

    /// <summary>Fade out, then hide to the tray and reset back to the main page.</summary>
    public void HideToTray()
    {
        if (_fadingOut || !IsVisible) return;
        _fadingOut = true;
        var anim = new DoubleAnimation(Opacity, 0, new Duration(TimeSpan.FromMilliseconds(130)));
        anim.Completed += (_, _) =>
        {
            _fadingOut = false;
            Hide();
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (DataContext is MainViewModel vm) vm.CurrentPage = Page.Main;
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => HideToTray();

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
