using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelDeck.Models;
using TunnelDeck.Services;
using TunnelDeck.Views;

namespace TunnelDeck.ViewModels;

public enum Page { Main, AddApp, Settings, Subscription, Connections }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly StateStore _store = new();
    private readonly SubscriptionService _subs = new();
    private readonly CoreBootstrapper _bootstrap = new();
    private readonly CoreController _core = new();
    private readonly TrafficStatsService _stats = new();
    private readonly LeakMonitor _leak = new();
    private readonly UpdateService _update = new();
    private UpdateInfo? _pendingUpdate;

    private AppState _state = new();
    private bool _coreReady;
    private bool _loading;   // suppress persist/apply side-effects while (re)hydrating

    public ObservableCollection<AppEntryViewModel> TunneledApps { get; } = new();
    public ObservableCollection<SiteEntryViewModel> TunneledSites { get; } = new();
    public ObservableCollection<ServerItemViewModel> Servers { get; } = new();
    public ObservableCollection<ConnItemViewModel> ActiveConnections { get; } = new();
    public bool HasConnections => ActiveConnections.Count > 0;
    private readonly DispatcherTimer _connTimer;
    public string[] LogLevels { get; } = { "trace", "debug", "info", "warn", "error" };

    // Leak / tunnel-down warning (shown as a red banner while connected)
    [ObservableProperty] private bool _leakWarning;
    [ObservableProperty] private string _leakWarningText = "";
    [ObservableProperty] private string _leakWarningSub = "";

    [ObservableProperty] private string _addSiteInput = "";

    // Total tunnel throughput (shown in the connect card while connected)
    [ObservableProperty] private bool _showSpeed;
    [ObservableProperty] private string _totalDownText = "0 Б/с";
    [ObservableProperty] private string _totalUpText = "0 Б/с";

    // Session duration + cumulative traffic (while connected)
    private readonly DispatcherTimer _sessionTimer;
    private DateTime _sessionStart;
    [ObservableProperty] private bool _showDuration;
    [ObservableProperty] private string _sessionDurationText = "00:00:00";
    [ObservableProperty] private string _sessionDownText = "0 Б";
    [ObservableProperty] private string _sessionUpText = "0 Б";

    // Check result popup (exit IP + flag)
    [ObservableProperty] private bool _checkResultVisible;
    [ObservableProperty] private bool _checkBusy;
    [ObservableProperty] private string _checkIp = "";
    [ObservableProperty] private string _checkLoc = "";
    [ObservableProperty] private System.Windows.Media.ImageSource? _checkFlagImage;
    public bool CheckHasFlag => CheckFlagImage is not null;
    partial void OnCheckFlagImageChanged(System.Windows.Media.ImageSource? value) => OnPropertyChanged(nameof(CheckHasFlag));

    // Auto-update
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateVersionText = "";
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string _updateStatus = "";

    [ObservableProperty] private Page _currentPage = Page.Main;
    [ObservableProperty] private AddAppViewModel? _addApp;
    [ObservableProperty] private string _subscriptionInput = "";

    [ObservableProperty] private ServerItemViewModel? _selectedServerItem;
    [ObservableProperty] private ConnectionStatus _status = ConnectionStatus.Disconnected;

    /// <summary>The currently selected server config (from the selected dropdown item).</summary>
    public ServerConfig? SelectedServer => SelectedServerItem?.Config;
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
    [ObservableProperty] private string _language = "system";

    public bool IsConnected => Status is ConnectionStatus.Connected;
    public bool HasSubscription => Servers.Count > 0;
    public bool HasApps => TunneledApps.Count > 0;
    public bool HasSites => TunneledSites.Count > 0;
    public bool NothingTunneled => !HasApps && !HasSites;

    public bool IsActive => Status is ConnectionStatus.Connected
        or ConnectionStatus.Connecting or ConnectionStatus.Reconnecting;

    public string ConnectLabel => IsActive ? Loc.T("S.Disconnect") : Loc.T("S.Connect");

    private static string StatusWord(ConnectionStatus s) => Loc.T(s switch
    {
        ConnectionStatus.Connected => "S.St.Connected",
        ConnectionStatus.Connecting => "S.St.Connecting",
        ConnectionStatus.Reconnecting => "S.St.Reconnecting",
        ConnectionStatus.Error => "S.St.Error",
        _ => "S.St.Off"
    });

    public System.Windows.Media.Brush StatusBrush => new System.Windows.Media.SolidColorBrush(
        Status switch
        {
            ConnectionStatus.Connected => System.Windows.Media.Color.FromRgb(0x0F, 0x7B, 0x0F),
            ConnectionStatus.Connecting => System.Windows.Media.Color.FromRgb(0x9D, 0x5D, 0x00),
            ConnectionStatus.Reconnecting => System.Windows.Media.Color.FromRgb(0x9D, 0x5D, 0x00),
            ConnectionStatus.Error => System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C),
            _ => System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A)
        });

    /// <summary>Background for the connect button: blue = connect, red = disconnect, amber = busy.</summary>
    public System.Windows.Media.Brush ConnectButtonBrush => new System.Windows.Media.SolidColorBrush(
        Status switch
        {
            ConnectionStatus.Connected => System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C),   // red (Отключить)
            ConnectionStatus.Connecting => System.Windows.Media.Color.FromRgb(0xB0, 0x71, 0x05),  // amber (busy)
            ConnectionStatus.Reconnecting => System.Windows.Media.Color.FromRgb(0xB0, 0x71, 0x05),
            _ => System.Windows.Media.Color.FromRgb(0x00, 0x5F, 0xB8)                              // blue (Подключить)
        });

    public MainViewModel()
    {
        _core.StatusChanged += (_, e) => OnUi(() =>
        {
            Status = e.Status;
            StatusText = StatusWord(e.Status);
            // When connected, the server/country is already shown in the dropdown,
            // so we skip the "· <country>" detail line to keep the card light.
            StatusDetail = e.Status == ConnectionStatus.Connected ? "" : (e.Message ?? "");
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(ConnectLabel));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(ConnectButtonBrush));
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
            SessionUpText = TrafficStatsService.FormatBytes(s.sessUp);
            SessionDownText = TrafficStatsService.FormatBytes(s.sessDown);
        });

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) =>
        {
            var t = DateTime.UtcNow - _sessionStart;
            SessionDurationText = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        };

        _connTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _connTimer.Tick += (_, _) => RefreshConnections();

        _leak.StatusChanged += (_, s) => OnUi(() =>
        {
            switch (s)
            {
                case LeakStatus.Leaking:
                    LeakWarning = true; LeakWarningText = Loc.T("S.LeakWarn"); LeakWarningSub = Loc.T("S.LeakWarnSub"); break;
                case LeakStatus.TunnelDown:
                    LeakWarning = true; LeakWarningText = Loc.T("S.LeakDown"); LeakWarningSub = Loc.T("S.LeakDownSub"); break;
                default:
                    LeakWarning = false; break;
            }
        });

        Loc.Changed += (_, _) => OnUi(() =>
        {
            StatusText = StatusWord(Status);
            OnPropertyChanged(nameof(ConnectLabel));
        });
    }

    private void StartStats()
    {
        ShowSpeed = true;
        _stats.Start();

        _sessionStart = DateTime.UtcNow;
        SessionDurationText = "00:00:00";
        SessionDownText = SessionUpText = "0 Б";
        ShowDuration = true;
        _sessionTimer.Start();

        LeakWarning = false;
        _leak.Start();
    }

    private void StopStats()
    {
        _stats.Stop();
        ShowSpeed = false;
        TotalUpText = TotalDownText = "0 Б/с";

        _sessionTimer.Stop();
        ShowDuration = false;

        _leak.Stop();
        LeakWarning = false;
        _connTimer.Stop();
        ActiveConnections.Clear();
        OnPropertyChanged(nameof(HasConnections));

        // Reset the check plate when the tunnel drops.
        CheckResultVisible = false;
    }

    public event EventHandler<ConnectionStatus>? ConnectionChanged;

    // ---- Lifecycle -------------------------------------------------------

    public async Task InitializeAsync()
    {
        _state = _store.Load();
        Loc.Apply(Loc.Resolve(_state.Settings.Language));
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

        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var info = await _update.CheckAsync();
            if (info is not null)
                OnUi(() =>
                {
                    _pendingUpdate = info;
                    UpdateVersionText = $"Доступно обновление {info.Version}";
                    UpdateAvailable = true;
                });
        }
        catch { /* offline / rate-limited — ignore */ }
    }

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_pendingUpdate is null || IsUpdating) return;
        try
        {
            IsUpdating = true;
            var progress = new Progress<int>(p => OnUi(() => UpdateStatus = $"Загрузка обновления… {p}%"));
            var path = await _update.DownloadInstallerAsync(_pendingUpdate, progress);
            UpdateStatus = "Запуск установки…";
            await ShutdownAsync();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatus = "Ошибка обновления: " + ex.Message;
            IsUpdating = false;
        }
    }

    /// <summary>Open the GitHub release page for the pending update in the browser.</summary>
    [RelayCommand]
    private void OpenReleaseNotes()
    {
        var url = _pendingUpdate?.HtmlUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }

    private void RebuildFromState()
    {
        _loading = true;

        Servers.Clear();
        foreach (var s in _state.Servers)
        {
            s.Name = TextUtil.StripEmoji(s.Name);   // clean already-persisted names
            Servers.Add(new ServerItemViewModel(s));
        }
        SelectedServerItem = Servers.FirstOrDefault(v => v.Config.Id == _state.SelectedServerId)
                             ?? Servers.FirstOrDefault();

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
        Language = _state.Settings.Language;

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
    private void GoConnections()
    {
        CurrentPage = Page.Connections;
        RefreshConnections();
    }

    partial void OnCurrentPageChanged(Page value)
    {
        if (value == Page.Connections) _connTimer.Start();
        else _connTimer.Stop();
    }

    private async void RefreshConnections()
    {
        if (!IsConnected)
        {
            OnUi(() => { ActiveConnections.Clear(); OnPropertyChanged(nameof(HasConnections)); });
            return;
        }
        try
        {
            var list = await _stats.FetchConnectionsAsync();
            OnUi(() =>
            {
                ActiveConnections.Clear();
                foreach (var c in list.Take(25)) ActiveConnections.Add(new ConnItemViewModel(c));
                OnPropertyChanged(nameof(HasConnections));
            });
        }
        catch { /* transient */ }
    }

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

    /// <summary>Query the exit IP/country THROUGH the VPN proxy to confirm the tunnel works.</summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        if (!IsConnected) { StatusDetail = "Сначала подключитесь."; return; }
        try
        {
            // Show the popup immediately in a loading state, then fill it in.
            CheckBusy = true;
            CheckIp = "";
            CheckLoc = "";
            CheckFlagImage = null;
            CheckResultVisible = true;

            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"socks5://{SingBoxConfigBuilder.SocksEndpoint}"),
                UseProxy = true
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "curl/8");
            var trace = await http.GetStringAsync("https://www.cloudflare.com/cdn-cgi/trace");
            var ip = TraceField(trace, "ip");
            var loc = TraceField(trace, "loc");

            CheckBusy = false;
            if (string.IsNullOrEmpty(ip)) { CheckIp = "—"; return; }
            CheckIp = ip;
            CheckLoc = loc;
            CheckFlagImage = FlagFactory.For(loc);
        }
        catch (Exception ex)
        {
            CheckBusy = false;
            CheckIp = "—";
            CheckLoc = "";
            StatusDetail = "Проверка не удалась: " + ex.Message;
        }
    }

    private static string TraceField(string trace, string key)
    {
        foreach (var line in trace.Split('\n'))
            if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return line[(key.Length + 1)..].Trim();
        return "";
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

    private long _lastPingTicks;
    private bool _pinging;

    /// <summary>Measure TCP ping for every server (called when the dropdown opens; throttled).</summary>
    public async void MeasurePings()
    {
        if (_pinging) return;
        // Throttle: at most once per 12 seconds.
        var now = Environment.TickCount64;
        if (_lastPingTicks != 0 && now - _lastPingTicks < 12_000) return;
        _lastPingTicks = now;
        _pinging = true;
        try
        {
            var items = Servers.Where(v => v.Config.Server != "0.0.0.0" && !string.IsNullOrWhiteSpace(v.Config.Server)).ToList();
            foreach (var v in items) { var vv = v; OnUi(() => vv.PingMs = -3); }
            await Task.WhenAll(items.Select(async v =>
            {
                var ms = await PingService.TcpPingAsync(v.Config.Server, v.Config.Port);
                OnUi(() => v.PingMs = ms >= 0 ? ms : -1);
            }));
        }
        finally { _pinging = false; }
    }

    partial void OnSelectedServerItemChanged(ServerItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedServer));
        if (_loading || value is null) return;
        _state.SelectedServerId = value.Config.Id;
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

    partial void OnLanguageChanged(string value)
    {
        if (_loading || string.IsNullOrWhiteSpace(value)) return;
        _state.Settings.Language = value;
        Loc.Apply(Loc.Resolve(value));
        Persist();
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
        StatusText = Loc.T("S.St.Connected");
        StatusDetail = "";   // real app also clears this when connected (server shown in dropdown)
        ShowSpeed = true;
        TotalDownText = "4,2 МБ/с";
        TotalUpText = "180 КБ/с";
        ShowDuration = true;
        SessionDurationText = "00:12:34";
        SessionDownText = "1,84 ГБ";
        SessionUpText = "96,3 МБ";
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ConnectLabel));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(ConnectButtonBrush));
        ConnectionChanged?.Invoke(this, ConnectionStatus.Connected);

        // DEMO=2 also shows the check mini-plate + ping badges (for visual verification).
        if (Environment.GetEnvironmentVariable("TUNNELDECK_DEMO") == "2")
        {
            StatusDetail = "";
            CheckBusy = false;
            CheckIp = "146.70.28.14";
            CheckLoc = "NL";
            CheckFlagImage = FlagFactory.For("NL");
            CheckResultVisible = true;
            var demoPings = new[] { 38, 120, 260 };
            for (int i = 0; i < Servers.Count; i++) Servers[i].PingMs = demoPings[i % demoPings.Length];

            ActiveConnections.Clear();
            foreach (var c in new[]
            {
                new TrafficStatsService.ConnectionInfo("youtube.com", "tcp · 443", 210_000, 4_200_000),
                new TrafficStatsService.ConnectionInfo("discord.com", "tcp · 443", 88_000, 640_000),
                new TrafficStatsService.ConnectionInfo("cloudflare.com", "tcp · 443", 12_000, 96_000),
            }) ActiveConnections.Add(new ConnItemViewModel(c));
            OnPropertyChanged(nameof(HasConnections));

            if (Environment.GetEnvironmentVariable("TUNNELDECK_LEAK") == "1")
            {
                LeakWarning = true;
                LeakWarningText = Loc.T("S.LeakWarn");
                LeakWarningSub = Loc.T("S.LeakWarnSub");
            }
        }
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
