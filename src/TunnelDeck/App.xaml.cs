using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon;
using TunnelDeck.Models;
using TunnelDeck.Services;
using TunnelDeck.ViewModels;
using TunnelDeck.Views;

namespace TunnelDeck;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TaskbarIcon? _tray;
    private MainViewModel? _vm;
    private FlyoutWindow? _flyout;
    private volatile bool _shuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never let an unhandled exception hard-crash the tray app.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log("AppDomain", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log("Task", ev.Exception); ev.SetObserved(); };

        // Test hook: a suffix lets a debug instance coexist with the installed app.
        var instance = Environment.GetEnvironmentVariable("TUNNELDECK_INSTANCE");
        var mutexName = string.IsNullOrEmpty(instance) ? "TunnelDeck.SingleInstance" : $"TunnelDeck.SingleInstance.{instance}";
        _singleInstance = new Mutex(true, mutexName, out bool isNew);
        if (!isNew) { Shutdown(); return; }

        Paths.EnsureDirs();
        ThemeService.Init();   // follow the Windows light/dark setting

        _vm = new MainViewModel();
        _vm.ConnectionChanged += (_, status) => { UpdateTray(status); Notify(status); };

        _flyout = new FlyoutWindow { DataContext = _vm };

        SetupTray();

        await _vm.InitializeAsync();

        // Show the window on a normal launch. Start hidden in the tray only when
        // launched with --tray (the "start with Windows" autostart entry passes this).
        var startHidden = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        if (!startHidden)
            ShowFlyout();

        // Test hook: jump to a page for screenshot verification.
        switch (Environment.GetEnvironmentVariable("TUNNELDECK_PAGE"))
        {
            case "settings": _vm.GoSettingsCommand.Execute(null); break;
            case "addapp": _vm.GoAddAppCommand.Execute(null); break;
            case "sub": _vm.GoSubscriptionCommand.Execute(null); break;
        }

        var demo = Environment.GetEnvironmentVariable("TUNNELDECK_DEMO");
        if (demo is "1" or "2")
            _vm.SetDemoConnected();
    }

    private void SetupTray()
    {
        _tray = new TaskbarIcon
        {
            IconSource = TrayIconFactory.For(ConnectionStatus.Disconnected),
            ToolTipText = "TunnelDeck — выключено",
            ContextMenu = BuildContextMenu()
        };
        _tray.TrayLeftMouseUp += (_, _) => ToggleFlyout();
        _tray.ForceCreate();
    }

    private System.Windows.Controls.MenuItem? _serversMenu;

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var open = new System.Windows.Controls.MenuItem { Header = "Открыть" };
        open.Click += (_, _) => ShowFlyout();

        var toggle = new System.Windows.Controls.MenuItem { Header = "Подключить / Отключить" };
        toggle.Click += async (_, _) => { try { if (_vm is not null) await _vm.ToggleConnectionCommand.ExecuteAsync(null); } catch (Exception ex) { Log("Toggle", ex); } };

        _serversMenu = new System.Windows.Controls.MenuItem { Header = "Серверы" };

        var settings = new System.Windows.Controls.MenuItem { Header = "Настройки" };
        settings.Click += (_, _) => { ShowFlyout(); _vm?.GoSettingsCommand.Execute(null); };

        var quit = new System.Windows.Controls.MenuItem { Header = "Выход" };
        quit.Click += async (_, _) => await QuitAsync();

        menu.Items.Add(open);
        menu.Items.Add(toggle);
        menu.Items.Add(_serversMenu);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(settings);
        menu.Items.Add(quit);

        // Rebuild the servers submenu each time so it reflects current list + selection.
        menu.Opened += (_, _) => RebuildServersMenu();
        return menu;
    }

    private void RebuildServersMenu()
    {
        if (_serversMenu is null || _vm is null) return;
        _serversMenu.Items.Clear();

        if (_vm.Servers.Count == 0)
        {
            _serversMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Нет серверов", IsEnabled = false });
            _serversMenu.IsEnabled = false;
            return;
        }
        _serversMenu.IsEnabled = true;

        foreach (var s in _vm.Servers)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = s.Name,
                IsCheckable = true,
                IsChecked = ReferenceEquals(s, _vm.SelectedServerItem)
            };
            if (s.FlagImage is not null)
                item.Icon = new System.Windows.Controls.Image { Source = s.FlagImage, Width = 20, Height = 13 };

            var server = s;
            item.Click += async (_, _) =>
            {
                try
                {
                    _vm.SelectedServerItem = server;                 // switches live if connected
                    if (!_vm.IsActive) await _vm.ConnectAsync();      // otherwise connect to it
                }
                catch (Exception ex) { Log("QuickSwitch", ex); }
            };
            _serversMenu.Items.Add(item);
        }
    }

    private void UpdateTray(ConnectionStatus status)
    {
        if (_tray is null || _shuttingDown) return;
        try
        {
        _tray.IconSource = TrayIconFactory.For(status);
        _tray.ToolTipText = status switch
        {
            ConnectionStatus.Connected => "TunnelDeck — подключено",
            ConnectionStatus.Connecting => "TunnelDeck — подключение…",
            ConnectionStatus.Reconnecting => "TunnelDeck — переподключение…",
            ConnectionStatus.Error => "TunnelDeck — ошибка",
            _ => "TunnelDeck — выключено"
        };
        }
        catch (ObjectDisposedException) { /* tray gone during shutdown */ }
        catch (Exception ex) { Log("UpdateTray", ex); }
    }

    private ConnectionStatus _lastNotified = ConnectionStatus.Disconnected;

    private void Notify(ConnectionStatus status)
    {
        if (_tray is null || _shuttingDown || status == _lastNotified) return;
        var prev = _lastNotified;
        _lastNotified = status;
        try
        {
            switch (status)
            {
                case ConnectionStatus.Connected:
                    _tray.ShowNotification("TunnelDeck", _vm?.StatusDetail is { Length: > 0 } d ? d : "Подключено");
                    break;
                case ConnectionStatus.Error:
                    _tray.ShowNotification("TunnelDeck — ошибка", _vm?.StatusDetail is { Length: > 0 } e ? e : "Не удалось подключиться");
                    break;
                case ConnectionStatus.Disconnected when prev == ConnectionStatus.Connected:
                    _tray.ShowNotification("TunnelDeck", "VPN отключён");
                    break;
            }
        }
        catch (Exception ex) { Log("Notify", ex); }
    }

    private void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true }) _flyout.HideToTray();
        else ShowFlyout();
    }

    private void ShowFlyout()
    {
        if (_flyout is null) return;
        _flyout.PositionNearTray();
        _flyout.Show();
        _flyout.Activate();
        _flyout.Topmost = true;
        _flyout.Focus();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Dispatcher", e.Exception);
        MessageBox.Show(
            "Произошла ошибка:\n\n" + e.Exception.Message +
            "\n\nПодробности записаны в журнал. Приложение продолжит работу.",
            "TunnelDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void Log(string source, Exception? ex)
    {
        if (ex is null) return;
        try
        {
            File.AppendAllText(Paths.LogFile,
                $"[{source}] {DateTime.Now:HH:mm:ss} {ex}\n{new string('-', 60)}\n");
        }
        catch { }
    }

    private async Task QuitAsync()
    {
        _shuttingDown = true;
        if (_vm is not null) await _vm.ShutdownAsync();
        _tray?.Dispose();
        Shutdown();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        if (_vm is not null) await _vm.ShutdownAsync();
        _tray?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
