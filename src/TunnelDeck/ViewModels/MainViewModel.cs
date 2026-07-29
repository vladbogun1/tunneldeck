using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelDeck.Models;
using TunnelDeck.Services;
using TunnelDeck.Views;

namespace TunnelDeck.ViewModels;

public enum Page { Main, AddApp, Settings, Subscription }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly StateStore _store = new();
    private readonly SubscriptionService _subs = new();
    private readonly CoreBootstrapper _bootstrap = new();
    private readonly CoreController _core = new();
    private readonly TrafficStatsService _stats = new();

    private AppState _state = new();
    private bool _coreReady;
    private bool _loading;   // suppress persist/apply side-effects while (re)hydrating

    public ObservableCollection<AppEntryViewModel> TunneledApps { get; } = new();
    public ObservableCollection<SiteEntryViewModel> TunneledSites { get; } = new();
    public ObservableCollection<ServerConfig> Servers { get; } = new();
    public string[] LogLevels { get; } = { "trace", "debug", "info", "warn", "error" };

    [ObservableProperty] private string _addSiteInput = "";

    // Total tunnel throughput (shown in the connect card while connected)
    [ObservableProperty] private bool _showSpeed;
    [ObservableProperty] private string _totalDownText = "0 Б/с";
    [ObservableProperty] private string _totalUpText = "0 Б/с";

    [ObservableProperty] private Page _currentPage = Page.Main;
    [ObservableProperty] private AddAppViewModel? _addApp;
    [ObservableProperty] private string _subscriptionInput = "";

    [ObservableProperty] private ServerConfig? _selectedServer;
    [ObservableProperty] private ConnectionStatus _status = ConnectionStatus.Disconnected;
    [ObservableProperty] private string _statusText = "Выключено";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _coreVersion = $"sing-box {CoreBootstrapper.Version}";

    // Settings proxies (apply immediately, Windows 11 style)
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _autoConnectOnLaunch;
    [ObservableProperty] private bool _killSwitch = true;
    [ObservableProperty] private bool _proxyDns = true;
    [ObservableProperty] private string _logLevel = "warn";

    public bool IsConnected => Status is ConnectionStatus.Connected;
    public bool HasSubscription => Servers.Count > 0;
    public bool HasApps => TunneledApps.Count > 0;
    public bool HasSites => TunneledSites.Count > 0;
    public bool NothingTunneled => !HasApps && !HasSites;

    public bool IsActive => Status is ConnectionStatus.Connected
        or ConnectionStatus.Connecting or ConnectionStatus.Reconnecting;

    public string ConnectLabel => IsActive ? "Отключить" : "Подключить";

    public System.Windows.Media.Brush StatusBrush => new System.Windows.Media.SolidColorBrush(
        Status switch
        {
            ConnectionStatus.Connected => System.Windows.Media.Color.FromRgb(0x0F, 0x7B, 0x0F),
            ConnectionStatus.Connecting => System.Windows.Media.Color.FromRgb(0x9D, 0x5D, 0x00),
            ConnectionStatus.Reconnecting => System.Windows.Media.Color.FromRgb(0x9D, 0x5D, 0x00),
            ConnectionStatus.Error => System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C),
            _ => System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A)
        });

    public MainViewModel()
    {
        _core.StatusChanged += (_, e) => OnUi(() =>
        {
            Status = e.Status;
            StatusText = e.Status switch
            {
                ConnectionStatus.Connected => "Подключено",
                ConnectionStatus.Connecting => "Подключение…",
                ConnectionStatus.Reconnecting => "Переподключение…",
                ConnectionStatus.Error => "Ошибка",
                _ => "Выключено"
            };
            StatusDetail = e.Message ?? "";
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(ConnectLabel));
            OnPropertyChanged(nameof(StatusBrush));
            ConnectionChanged?.Invoke(this, e.Status);

            if (e.Status == ConnectionStatus.Connected) StartStats();
            else StopStats();
        });

        TunneledApps.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasApps)); OnPropertyChanged(nameof(NothingTunneled)); };
        TunneledSites.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasSites)); OnPropertyChanged(nameof(NothingTunneled)); };

        _stats.Updated += (_, s) => OnUi(() =>
        {
            TotalUpText = TrafficStatsService.Format(s.up);
            TotalDownText = TrafficStatsService.Format(s.down);
        });
    }

    private void StartStats()
    {
        ShowSpeed = true;
        _stats.Start();
    }

    private void StopStats()
    {
        _stats.Stop();
        ShowSpeed = false;
        TotalUpText = TotalDownText = "0 Б/с";
    }

    public event EventHandler<ConnectionStatus>? ConnectionChanged;

    // ---- Lifecycle -------------------------------------------------------

    public async Task InitializeAsync()
    {
        _state = _store.Load();
        RebuildFromState();

        try
        {
            IsBusy = true;
            StatusDetail = "Подготовка ядра…";
            var progress = new Progress<string>(msg => OnUi(() => StatusDetail = msg));
            await _bootstrap.EnsureAsync(progress);
            await ProxiFyreBootstrapper.EnsureAsync(progress);
            _coreReady = true;

            StatusDetail = ProxiFyreBootstrapper.IsDriverInstalled
                ? ""
                : "Драйвер сетевого фильтра не установлен — переустановите TunnelDeck через установщик.";
        }
        catch (Exception ex)
        {
            StatusDetail = "Не удалось подготовить компоненты: " + ex.Message;
        }
        finally { IsBusy = false; }

        if (_coreReady && _state.Settings.AutoConnectOnLaunch && HasSubscription)
            await ConnectAsync();
    }

    private void RebuildFromState()
    {
        _loading = true;

        Servers.Clear();
        foreach (var s in _state.Servers)
        {
            s.Name = TextUtil.StripEmoji(s.Name);   // clean already-persisted names
            Servers.Add(s);
        }
        SelectedServer = _state.SelectedServer;

        TunneledApps.Clear();
        foreach (var app in _state.TunneledApps)
            TunneledApps.Add(Wrap(app));

        TunneledSites.Clear();
        foreach (var d in _state.TunneledSites)
            TunneledSites.Add(new SiteEntryViewModel(d));
        OnPropertyChanged(nameof(HasSites));

        StartWithWindows = _state.Settings.StartWithWindows;
        AutoConnectOnLaunch = _state.Settings.AutoConnectOnLaunch;
        KillSwitch = _state.Settings.KillSwitch;
        ProxyDns = _state.Settings.ProxyDnsForTunneledApps;
        LogLevel = _state.Settings.LogLevel;

        SubscriptionInput = _state.SubscriptionUrl;

        OnPropertyChanged(nameof(HasSubscription));
        OnPropertyChanged(nameof(HasApps));

        _loading = false;
    }

    private AppEntryViewModel Wrap(TunneledApp app)
    {
        var vm = new AppEntryViewModel(app);
        vm.EnabledChanged += (_, _) => OnUi(() => { Persist(); SafeApply(); });
        return vm;
    }

    // ---- Navigation ------------------------------------------------------

    [RelayCommand] private void GoBack() => CurrentPage = Page.Main;

    [RelayCommand]
    private void GoAddApp()
    {
        AddApp = new AddAppViewModel();
        CurrentPage = Page.AddApp;
    }

    [RelayCommand] private void GoSettings() => CurrentPage = Page.Settings;

    [RelayCommand]
    private void GoSubscription()
    {
        SubscriptionInput = _state.SubscriptionUrl;
        CurrentPage = Page.Subscription;
    }

    // ---- Connection ------------------------------------------------------

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsActive) await DisconnectAsync();
        else await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        if (!_coreReady) { StatusDetail = "Ядро ещё не готово."; return; }
        var server = SelectedServer;
        if (server is null) { StatusDetail = "Сначала добавьте ключ подписки."; return; }

        try
        {
            IsBusy = true;
            await _core.StartAsync(server, _state.TunneledApps, _state.Settings, _state.TunneledSites);
        }
        catch (Exception ex)
        {
            Status = ConnectionStatus.Error;
            StatusText = "Ошибка";
            StatusDetail = ex.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task DisconnectAsync()
    {
        IsBusy = true;
        try { await _core.StopAsync(); }
        catch (Exception ex) { StatusDetail = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task ApplyIfRunningAsync()
    {
        if (SelectedServer is null) return;
        await _core.ApplyAsync(SelectedServer, _state.TunneledApps, _state.Settings, _state.TunneledSites);
    }

    /// <summary>Fire-and-forget apply that never lets an exception escape (crash-safe).</summary>
    private async void SafeApply()
    {
        try { await ApplyIfRunningAsync(); }
        catch (Exception ex) { OnUi(() => StatusDetail = "Ошибка применения: " + ex.Message); }
    }

    // ---- Apps ------------------------------------------------------------

    [RelayCommand]
    private void AddSelectedRunning()
    {
        var sel = AddApp?.Selected;
        if (sel is null) return;
        AddTunneled(AddAppViewModel.FromRunning(sel));
        GoBack();
    }

    [RelayCommand]
    private void BrowseAndAdd()
    {
        var path = FileDialogService.PickExecutable();
        if (string.IsNullOrWhiteSpace(path)) return;
        AddTunneled(AddAppViewModel.FromExePath(path));
        GoBack();
    }

    private void AddTunneled(TunneledApp app)
    {
        if (_state.TunneledApps.Any(a =>
                string.Equals(a.ProcessName, app.ProcessName, StringComparison.OrdinalIgnoreCase)))
            return;

        _state.TunneledApps.Add(app);
        TunneledApps.Add(Wrap(app));
        Persist();
        SafeApply();
    }

    [RelayCommand]
    private void RemoveApp(AppEntryViewModel? entry)
    {
        if (entry is null) return;
        _state.TunneledApps.Remove(entry.Model);
        TunneledApps.Remove(entry);
        Persist();
        SafeApply();
    }

    // ---- Sites ----------------------------------------------------------

    [RelayCommand]
    private void AddSite()
    {
        var domain = NormalizeDomain(AddSiteInput);
        if (domain is null) { StatusDetail = "Введите корректный домен, например youtube.com"; return; }
        if (_state.TunneledSites.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)))
        {
            AddSiteInput = "";
            GoBack();
            return;
        }
        _state.TunneledSites.Add(domain);
        TunneledSites.Add(new SiteEntryViewModel(domain));
        AddSiteInput = "";
        OnPropertyChanged(nameof(HasSites));
        Persist();
        SafeApply();
        GoBack();
    }

    [RelayCommand]
    private void RemoveSite(SiteEntryViewModel? entry)
    {
        if (entry is null) return;
        _state.TunneledSites.RemoveAll(d => string.Equals(d, entry.Domain, StringComparison.OrdinalIgnoreCase));
        TunneledSites.Remove(entry);
        OnPropertyChanged(nameof(HasSites));
        Persist();
        SafeApply();
    }

    /// <summary>Reduce a user-entered URL/host to a bare domain (drop scheme/path/port/www).</summary>
    private static string? NormalizeDomain(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.Length == 0) return null;
        var slash = s.IndexOf("://", StringComparison.Ordinal);
        if (slash >= 0) s = s[(slash + 3)..];
        s = s.Split('/')[0].Split('?')[0].Split(':')[0].Trim().ToLowerInvariant();
        if (s.StartsWith("www.")) s = s[4..];
        // must contain a dot and only valid host characters
        if (!s.Contains('.') || s.Any(c => !(char.IsLetterOrDigit(c) || c == '.' || c == '-'))) return null;
        return s;
    }

    // ---- Subscription ----------------------------------------------------

    [RelayCommand]
    private async Task LoadSubscriptionAsync()
    {
        var url = (SubscriptionInput ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url)) { StatusDetail = "Вставьте ссылку подписки."; return; }

        try
        {
            IsBusy = true;
            StatusDetail = "Загрузка подписки…";
            var servers = await _subs.FetchAsync(url, EnsureHwid());
            if (servers.Count == 0) { StatusDetail = "Серверы не найдены."; return; }

            _state.SubscriptionUrl = url;
            _state.Servers = servers.ToList();
            if (_state.SelectedServerId is null || _state.Servers.All(s => s.Id != _state.SelectedServerId))
                _state.SelectedServerId = _state.Servers[0].Id;

            RebuildFromState();
            Persist();
            StatusDetail = $"Загружено серверов: {servers.Count}.";
            CurrentPage = Page.Main;
        }
        catch (Exception ex)
        {
            StatusDetail = "Ошибка подписки: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ---- Server selection ------------------------------------------------

    partial void OnSelectedServerChanged(ServerConfig? value)
    {
        if (_loading || value is null) return;
        _state.SelectedServerId = value.Id;
        Persist();
        SafeApply();
    }

    // ---- Settings (apply live) ------------------------------------------

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        _state.Settings.StartWithWindows = value;
        try { AutostartService.SetEnabled(value); } catch { }
        Persist();
    }

    partial void OnAutoConnectOnLaunchChanged(bool value)
    {
        if (_loading) return;
        _state.Settings.AutoConnectOnLaunch = value;
        Persist();
    }

    partial void OnKillSwitchChanged(bool value)
    {
        if (_loading) return;
        _state.Settings.KillSwitch = value;
        Persist();
        SafeApply();
    }

    partial void OnProxyDnsChanged(bool value)
    {
        if (_loading) return;
        _state.Settings.ProxyDnsForTunneledApps = value;
        Persist();
        SafeApply();
    }

    partial void OnLogLevelChanged(string value)
    {
        if (_loading || string.IsNullOrWhiteSpace(value)) return;
        _state.Settings.LogLevel = value;
        Persist();
        SafeApply();
    }

    // ---- Helpers ---------------------------------------------------------

    private string EnsureHwid()
    {
        if (string.IsNullOrWhiteSpace(_state.Hwid))
        {
            _state.Hwid = "td-" + Guid.NewGuid().ToString("N");
            Persist();
        }
        return _state.Hwid;
    }

    private void Persist()
    {
        try { _store.Save(_state); } catch { }
    }

    public async Task ShutdownAsync()
    {
        _stats.Stop();
        try { await _core.StopAsync(); } catch { }
    }

    /// <summary>Test-only: fake a connected + throughput state for screenshots/GIF.</summary>
    public void SetDemoConnected()
    {
        Status = ConnectionStatus.Connected;
        StatusText = "Подключено";
        StatusDetail = "Подключено · Польша";
        ShowSpeed = true;
        TotalDownText = "4,2 МБ/с";
        TotalUpText = "180 КБ/с";
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ConnectLabel));
        OnPropertyChanged(nameof(StatusBrush));
        ConnectionChanged?.Invoke(this, ConnectionStatus.Connected);
    }

    private static void OnUi(Action action)
    {
        try
        {
            var app = Application.Current;
            if (app is null) { action(); return; }
            if (app.Dispatcher.HasShutdownStarted) return;
            if (app.Dispatcher.CheckAccess()) action();
            else app.Dispatcher.BeginInvoke(action);
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText(Paths.LogFile, $"[OnUi] {DateTime.Now:HH:mm:ss} {ex}\n"); } catch { }
        }
    }
}
