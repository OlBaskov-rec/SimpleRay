using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SimpleRay.App.Engine;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Localization;
using SimpleRay.App.Services;
using SimpleRay.Core.Config;
using SimpleRay.Core.Dns;
using SimpleRay.Core.Engine;
using SimpleRay.Core.Models;
using SimpleRay.Core.Net;
using SimpleRay.Core.Profiles;
using SimpleRay.Core.Update;

namespace SimpleRay.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogChars = 20_000;

    private static LocalizationManager L => LocalizationManager.Instance;

    private readonly ProfileStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly UpdateService _updateService = new();
    private readonly SubscriptionService _subscriptionService = new();
    private readonly SubscriptionStore _subscriptionStore = new();
    private readonly List<SubscriptionEntry> _subscriptions;
    private readonly DispatcherTimer _subscriptionTimer;
    private bool _subscriptionsBusy;
    private readonly ConnectionController _connection;
    private readonly RoutingSettings _routing;
    private readonly Dispatcher _dispatcher;
    private readonly IDialogService _dialogs;
    private readonly ImportCoordinator _import = new();

    // geosite/geoip tags backing the routing presets (must exist as *.srs in GeoDir).
    private const string TagRuSites = "geosite-category-ru";
    private const string TagRuIp = "geoip-ru";
    private const string TagPrivate = "geosite-private";

    private ProfileConfig? _selectedProfile;
    private bool _isConnected;
    private string _statusText;
    private string _log = "";
    private string _activeLabel = "";

    public MainViewModel(IDialogService? dialogs = null, IKillSwitch? killSwitch = null)
    {
        _dialogs = dialogs ?? new MessageBoxDialogService();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _routing = _settingsStore.Load();
        _subscriptions = _subscriptionStore.Load();
        _statusText = L["status.disconnected"];

        foreach (var p in _store.Load())
            Profiles.Add(p);
        // Restore the last connected server as the selection (survives the elevated relaunch),
        // falling back to the first profile.
        _selectedProfile = Profiles.FirstOrDefault(p => p.Tag == _routing.LastProfileTag)
                           ?? Profiles.FirstOrDefault();

        // WfpKillSwitch validated on a real machine (harness Stage A: Engage/Disengage clean,
        // block confirmed, filters cleaned by deterministic key). Engaged on connect only when
        // the kill-switch setting is on; Engage failures never break the connection, and
        // CleanupLeftovers (App startup) + Disengage (disconnect/exit) + the non-boot-persistent
        // filters make it fail-closed but recoverable. The ConnectionController owns the engine +
        // watchdog + liveness + kill-switch lifecycle; the view-model just maps its events to UI.
        var engine = new SingBoxEngine(new EngineOptions
        {
            ExecutablePath = AppPaths.CoreExe,
            WorkingDirectory = AppPaths.RuntimeDir,
            Terminator = new ConsoleCtrlTerminator(),
        });
        _connection = new ConnectionController(engine, killSwitch ?? new WfpKillSwitch());
        _connection.StateChanged += OnEngineStateChanged;
        _connection.LogReceived += OnEngineLog;
        _connection.Reconnecting += (attempt, max) => _dispatcher.BeginInvoke(() =>
            StatusText = L.Format("status.reconnecting", attempt, max));
        _connection.GaveUpBlocked += () => _dispatcher.BeginInvoke(() =>
            StatusText = L["status.reconnectGaveUpBlocked"]);
        _connection.GaveUpDisengaged += () => _dispatcher.BeginInvoke(() =>
            StatusText = L["status.reconnectGaveUp"]);
        _connection.KillSwitchEngageFailed += ex => _dispatcher.BeginInvoke(() =>
            StatusText = L.Format("status.killSwitchFail", ex.Message));

        foreach (var provider in DnsCatalog.All)
            DnsProviders.Add(new DnsProviderItem(provider));
        // Seed the resolver on first run (and repair a settings file naming one we no
        // longer ship) so the picker always reflects what the config will actually use.
        if (DnsCatalog.Find(_routing.Dns.LocalProviderId) is null)
            _routing.Dns.LocalProviderId = DnsCatalog.InitialIdForLanguage(L.CurrentCulture);

        ConnectCommand = new RelayCommand(ToggleConnectionAsync);
        ImportClipboardCommand = new RelayCommand(ImportFromClipboard);
        ImportFileCommand = new RelayCommand(ImportFromFile);
        ImportQrFileCommand = new RelayCommand(ImportQrFromFile);
        ImportQrClipboardCommand = new RelayCommand(ImportQrFromClipboard);
        ImportQrWebcamCommand = new RelayCommand(ImportQrFromWebcam);
        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedProfile is not null);
        RenameCommand = new RelayCommand(RenameSelected, () => SelectedProfile is not null);
        PingCommand = new RelayCommand(PingAllAsync, () => Profiles.Count > 0);
        OpenAppsCommand = new RelayCommand(OpenAppRouting);
        DirectDomainsCommand = new RelayCommand(EditDirectDomains);
        ProxyDomainsCommand = new RelayCommand(EditProxyDomains);
        GroupChangedCommand = new RelayCommand(OnGroupChanged);
        CheckUpdatesCommand = new RelayCommand(CheckForUpdatesAsync);
        CheckDnsCommand = new RelayCommand(CheckDnsAsync);
        AddSubscriptionCommand = new RelayCommand(AddSubscriptionAsync);
        RefreshSubscriptionsCommand = new RelayCommand(RefreshSubscriptionsAsync,
            () => _subscriptions.Count > 0);

        // Auto-update: check every few minutes which subscriptions are due (provider's
        // profile-update-interval, else daily); the first check runs once the window is up, so
        // an update missed while the app was closed happens on launch.
        _subscriptionTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMinutes(15),
        };
        _subscriptionTimer.Tick += async (_, _) => await AutoUpdateSubscriptionsAsync();
        _subscriptionTimer.Start();
        _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () => await AutoUpdateSubscriptionsAsync());

        L.CultureChanged += OnCultureChanged;
        Profiles.CollectionChanged += (_, _) =>
        {
            PingCommand.RaiseCanExecuteChanged();
            // Adding/removing a server can change the in-group count, so the hint that
            // reports it must refresh too (e.g. removing a grouped server).
            OnPropertyChanged(nameof(GroupHint));
        };
    }

    // --- Localization -----------------------------------------------------

    public IReadOnlyList<LanguageOption> Languages => L.Languages;

    public string SelectedLanguage
    {
        get => L.CurrentCulture;
        set
        {
            if (value is not null && value != L.CurrentCulture)
                L.SetCulture(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Re-render culture-dependent, non-live-bound properties after a language switch.</summary>
    private void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(Modes));
        OnPropertyChanged(nameof(FailoverModes));
        OnPropertyChanged(nameof(GroupHint));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(SubscriptionInfo));
        // Probe results carry localized text ("unavailable", units); drop them rather
        // than leave the picker mixing two languages.
        foreach (var item in DnsProviders)
            item.ClearResult();
        if (!IsConnected)
            StatusText = L["status.disconnected"];
    }

    public string AppVersion => "v" + UpdateService.CurrentVersion.ToString(3);

    /// <summary>Public source repository — surfaced in About so users can obtain the source (GPL).</summary>
    public const string SourceUrl = "https://github.com/OlBaskov-rec/SimpleRay";

    public string AboutTitle => L["about.title"];

    /// <summary>
    /// About text including the GPL notice. GPL-3.0 asks an interactive program to show
    /// users their rights; this is that notice, built fresh so it follows the UI language.
    /// </summary>
    public string AboutText =>
        $"SimpleRay {AppVersion}\n" +
        "Copyright (C) 2026 OlBaskov-rec\n\n" +
        L["about.notice"] + "\n\n" +
        L.Format("about.source", SourceUrl) + "\n" +
        L["about.licenses"];

    public ObservableCollection<ProfileConfig> Profiles { get; } = new();

    public ProfileConfig? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value))
            {
                RemoveCommand.RaiseCanExecuteChanged();
                RenameCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SubscriptionInfo));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetField(ref _isConnected, value))
                OnPropertyChanged(nameof(ConnectButtonText));
        }
    }

    public string ConnectButtonText => IsConnected ? L["connect.disconnect"] : L["connect.connect"];

    public string StatusText
    {
        get => _statusText;
        private set { if (SetField(ref _statusText, value)) OnPropertyChanged(nameof(TrayToolTip)); }
    }

    public string Log
    {
        get => _log;
        private set => SetField(ref _log, value);
    }

    public RelayCommand ConnectCommand { get; }
    public RelayCommand ImportClipboardCommand { get; }
    public RelayCommand ImportFileCommand { get; }
    public RelayCommand ImportQrFileCommand { get; }
    public RelayCommand ImportQrClipboardCommand { get; }
    public RelayCommand ImportQrWebcamCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand PingCommand { get; }
    public RelayCommand OpenAppsCommand { get; }
    public RelayCommand DirectDomainsCommand { get; }
    public RelayCommand ProxyDomainsCommand { get; }
    public RelayCommand GroupChangedCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand CheckDnsCommand { get; }
    public RelayCommand AddSubscriptionCommand { get; }
    public RelayCommand RefreshSubscriptionsCommand { get; }

    private async Task AddSubscriptionAsync()
    {
        var dlg = new TextInputWindow(L["sub.promptTitle"], L["sub.promptText"],
            _subscriptions.LastOrDefault()?.Url ?? "")
        { Owner = Application.Current?.MainWindow };
        dlg.WithHint(L["sub.supportedTitle"], L["tip.subscriptionAdd"]);
        if (dlg.ShowDialog() != true) return;

        switch (SubscriptionLink.Normalize(dlg.Value, out var url))
        {
            case SubscriptionLinkStatus.HappEncrypted:
                StatusText = L["sub.happEncrypted"];
                return;
            case SubscriptionLinkStatus.Invalid:
                StatusText = L["sub.badUrl"];
                return;
        }

        // Optional per-subscription User-Agent, for providers that serve servers only to the
        // client they issued the link for. Re-adding a saved URL edits its value.
        var existing = _subscriptions.FirstOrDefault(s => s.Url == url);
        var uaDlg = new TextInputWindow(L["sub.promptTitle"],
            L.Format("sub.uaPrompt", SubscriptionService.DefaultUserAgent), existing?.UserAgent ?? "")
        { Owner = Application.Current?.MainWindow };
        uaDlg.WithHint(L["sub.supportedTitle"], L["tip.subscriptionAdd"]);
        if (uaDlg.ShowDialog() != true) return;
        var userAgent = SubscriptionService.NormalizeUserAgent(uaDlg.Value);
        if (existing is not null)
            existing.UserAgent = userAgent;

        await RunSubscriptionUpdateAsync(new[] { (url, userAgent) }, quiet: false);
    }

    private async Task RefreshSubscriptionsAsync()
    {
        if (_subscriptions.Count == 0)
        {
            StatusText = L["sub.none"];
            return;
        }
        await RunSubscriptionUpdateAsync(_subscriptions.Select(s => (s.Url, s.UserAgent)).ToList(), quiet: false);
    }

    /// <summary>Timer tick: refreshes only the subscriptions whose update interval has elapsed.</summary>
    private async Task AutoUpdateSubscriptionsAsync()
    {
        var now = DateTimeOffset.Now;
        var due = _subscriptions.Where(s => s.Info.IsUpdateDue(s.LastUpdated, now))
            .Select(s => (s.Url, s.UserAgent)).ToList();
        if (due.Count > 0)
            await RunSubscriptionUpdateAsync(due, quiet: true);
    }

    /// <summary>
    /// Single-flight wrapper: a manual refresh and the timer never fetch at the same time.
    /// <paramref name="quiet"/> (auto-update) keeps progress and failures out of the status line.
    /// </summary>
    private async Task RunSubscriptionUpdateAsync(IReadOnlyList<(string Url, string? UserAgent)> subs, bool quiet)
    {
        if (_subscriptionsBusy)
            return;
        _subscriptionsBusy = true;
        try
        {
            foreach (var (url, userAgent) in subs)
                await ImportSubscriptionAsync(url, userAgent, quiet);
        }
        finally
        {
            _subscriptionsBusy = false;
        }
    }

    /// <summary>
    /// Fetches a subscription and replaces the profiles that came from it (group membership,
    /// selection and the connected highlight preserved), then stores the provider's metadata.
    /// </summary>
    private async Task ImportSubscriptionAsync(string url, string? userAgent, bool quiet)
    {
        if (!quiet)
            StatusText = L["sub.loading"];
        SubscriptionContent content;
        try
        {
            content = await _subscriptionService.FetchAsync(url, userAgent);
        }
        catch (Exception ex)
        {
            if (!quiet)
                StatusText = L.Format("sub.loadFail", ex.Message);
            return;
        }

        if (content.Encrypted || content.Profiles.Count == 0)
        {
            // Keep the existing servers: an empty or unreadable answer is more likely a
            // provider hiccup than a deliberate wipe.
            if (!quiet)
                StatusText = content.Encrypted ? L["sub.encrypted"]
                    : content.Skipped > 0 ? L.Format("sub.allSkipped", content.Skipped)
                    : L["sub.empty"];
            return;
        }

        var fetched = content.Profiles;
        // Preserve group membership across a refresh (match by Raw).
        var inGroup = Profiles.Where(p => p.InGroup && p.Raw is not null)
            .Select(p => p.Raw!).ToHashSet();
        var selectedRaw = SelectedProfile?.Raw;
        var activeRaw = Profiles.FirstOrDefault(p => p.IsActive)?.Raw;

        foreach (var stale in Profiles.Where(p => p.Subscription == url).ToList())
            Profiles.Remove(stale);

        foreach (var p in fetched)
        {
            if (p.Raw is not null && inGroup.Contains(p.Raw))
                p.InGroup = true;
            if (p.Raw is not null && p.Raw == activeRaw)
                p.IsActive = true;
            Profiles.Add(p);
        }

        if (SelectedProfile is null || !Profiles.Contains(SelectedProfile))
            SelectedProfile = Profiles.FirstOrDefault(p => p.Raw is not null && p.Raw == selectedRaw)
                              ?? Profiles.FirstOrDefault();
        SaveProfiles();

        var entry = _subscriptions.FirstOrDefault(s => s.Url == url);
        if (entry is null)
        {
            entry = new SubscriptionEntry { Url = url, UserAgent = userAgent };
            _subscriptions.Add(entry);
            RefreshSubscriptionsCommand.RaiseCanExecuteChanged();
        }
        entry.Info = content.Metadata;
        entry.LastUpdated = DateTimeOffset.Now;
        try { _subscriptionStore.Save(_subscriptions); }
        catch (Exception ex) { StatusText = L.Format("status.saveSettingsFail", ex.Message); }

        OnPropertyChanged(nameof(GroupHint));
        OnPropertyChanged(nameof(SubscriptionInfo));
        if (!quiet)
        {
            var name = content.Metadata.Title is { } t ? t + ": " : "";
            StatusText = name + (content.Skipped > 0
                ? L.Format("sub.updatedSkipped", fetched.Count, content.Skipped)
                : L.Format("sub.updated", fetched.Count));
        }
    }

    /// <summary>
    /// Provider info for the selected server's subscription: title, traffic used/quota, expiry
    /// and announcement, one per line; null when the server isn't from a subscription.
    /// </summary>
    public string? SubscriptionInfo
    {
        get
        {
            var url = SelectedProfile?.Subscription;
            var entry = url is null ? null : _subscriptions.FirstOrDefault(s => s.Url == url);
            if (entry is null)
                return null;
            var info = entry.Info;
            var lines = new List<string>();
            if (info.Title is not null)
                lines.Add(info.Title);
            if (info.Used is long used)
                lines.Add(info.Total is long total
                    ? L.Format("sub.info.traffic", FormatBytes(used), FormatBytes(total))
                    : L.Format("sub.info.trafficUnlimited", FormatBytes(used)));
            if (info.Expire is DateTimeOffset expire)
                lines.Add(expire <= DateTimeOffset.Now
                    ? L["sub.info.expired"]
                    : L.Format("sub.info.expires", expire.ToLocalTime().ToString("d"))); // UI culture: SetCulture sets CurrentCulture
            if (info.Announce is not null)
                lines.Add(info.Announce);
            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return u == 0 ? $"{bytes} B" : $"{v:0.##} {units[u]}";
    }

    private async Task CheckForUpdatesAsync()
    {
        if (IsConnected)
        {
            StatusText = L["status.updateConnected"];
            return;
        }

        StatusText = L["status.updateChecking"];
        ReleaseInfo? info;
        try
        {
            info = await _updateService.CheckAsync();
        }
        catch (Exception ex)
        {
            StatusText = L.Format("status.updateCheckFail", ex.Message);
            return;
        }

        if (info is null)
        {
            StatusText = L.Format("status.updateLatest", AppVersion);
            return;
        }

        var consent = _dialogs.Confirm(
            L.Format("update.consent", info.Version.ToString(3), UpdateService.CurrentVersion.ToString(3)),
            L["update.consentTitle"]);
        if (!consent)
        {
            StatusText = L["status.updateDeferred"];
            return;
        }

        try
        {
            var progress = new Progress<double>(p => StatusText = L.Format("status.updateDownloading", p.ToString("P0")));
            var staging = await _updateService.DownloadAndStageAsync(info, progress);

            StatusText = L["status.updateApplying"];
            await _connection.ShutdownAsync(); // stop sing-box so its files aren't locked
            _updateService.ApplyAndRestart(staging);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText = L.Format("status.updateError", ex.Message);
        }
    }

    // --- Failover group ---------------------------------------------------

    public sealed record FailoverModeItem(string Label, FailoverMode Mode);

    public IReadOnlyList<FailoverModeItem> FailoverModes => new[]
    {
        new FailoverModeItem(L["failover.urltest"], FailoverMode.UrlTest),
        new FailoverModeItem(L["failover.selector"], FailoverMode.Selector),
    };

    public FailoverMode SelectedFailoverMode
    {
        get => _routing.Failover.Mode;
        set
        {
            if (_routing.Failover.Mode == value) return;
            _routing.Failover.Mode = value;
            OnPropertyChanged();
            SaveRouting();
        }
    }

    /// <summary>Hint about the current failover group composition.</summary>
    public string GroupHint
    {
        get
        {
            int n = Profiles.Count(p => p.InGroup);
            return n >= 2 ? L.Format("group.hintActive", n) : L["group.hintInactive"];
        }
    }

    /// <summary>Called when a profile's group membership toggles: persist + refresh hint.</summary>
    private void OnGroupChanged()
    {
        SaveProfiles();
        OnPropertyChanged(nameof(GroupHint));
    }

    private void OpenAppRouting()
    {
        var dlg = new AppRoutingWindow(_routing) { Owner = Application.Current?.MainWindow };
        dlg.ShowDialog();
        SaveRouting(); // dialog mutated _routing in place
    }

    private void EditDirectDomains()
    {
        var dlg = new DomainsWindow(_routing.DirectDomains) { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            _routing.DirectDomains = dlg.Domains;
            SaveRouting();
        }
    }

    private void EditProxyDomains()
    {
        var dlg = new DomainsWindow(_routing.ProxyDomains, L["proxyDomains.title"], L["proxyDomains.hint"])
        {
            Owner = Application.Current?.MainWindow,
        };
        if (dlg.ShowDialog() == true)
        {
            _routing.ProxyDomains = dlg.Domains;
            SaveRouting();
        }
    }

    // --- Routing settings -------------------------------------------------

    public sealed record RoutingModeItem(string Label, RoutingMode Mode);

    public IReadOnlyList<RoutingModeItem> Modes => new[]
    {
        new RoutingModeItem(L["mode.global"], RoutingMode.Global),
        new RoutingModeItem(L["mode.rule"], RoutingMode.Rule),
        new RoutingModeItem(L["mode.direct"], RoutingMode.Direct),
    };

    public RoutingMode SelectedMode
    {
        get => _routing.Mode;
        set
        {
            if (_routing.Mode == value) return;
            _routing.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRuleMode));
            SaveRouting();
        }
    }

    /// <summary>Geo-based direct rules only apply in Rule mode; used to enable/disable toggles.</summary>
    public bool IsRuleMode => _routing.Mode == RoutingMode.Rule;

    /// <summary>Russian sites bypass the VPN (geosite-category-ru).</summary>
    public bool DirectRuSites
    {
        get => _routing.DirectGeosite.Contains(TagRuSites);
        set { if (ToggleTag(_routing.DirectGeosite, TagRuSites, value)) { OnPropertyChanged(); SaveRouting(); } }
    }

    /// <summary>Russian IP ranges bypass the VPN (geoip-ru).</summary>
    public bool DirectRuIp
    {
        get => _routing.DirectGeoip.Contains(TagRuIp);
        set { if (ToggleTag(_routing.DirectGeoip, TagRuIp, value)) { OnPropertyChanged(); SaveRouting(); } }
    }

    /// <summary>LAN / private addresses bypass the VPN (geosite-private).</summary>
    public bool DirectLan
    {
        get => _routing.DirectGeosite.Contains(TagPrivate);
        set { if (ToggleTag(_routing.DirectGeosite, TagPrivate, value)) { OnPropertyChanged(); SaveRouting(); } }
    }

    /// <summary>Block ads/trackers (geosite-category-ads-all); applies in all modes.</summary>
    public bool BlockAds
    {
        get => _routing.BlockAds;
        set { if (_routing.BlockAds != value) { _routing.BlockAds = value; OnPropertyChanged(); SaveRouting(); } }
    }

    /// <summary>Connect automatically when the app starts.</summary>
    public bool AutoConnect
    {
        get => _routing.AutoConnect;
        set { if (_routing.AutoConnect != value) { _routing.AutoConnect = value; OnPropertyChanged(); SaveRouting(); } }
    }

    /// <summary>Block all non-tunnel traffic while connected (kill-switch).</summary>
    public bool KillSwitchEnabled
    {
        get => _routing.KillSwitch;
        set { if (_routing.KillSwitch != value) { _routing.KillSwitch = value; OnPropertyChanged(); SaveRouting(); } }
    }

    /// <summary>Enable IPv6 (dual-stack). Off by default; see RoutingSettings.EnableIpv6.</summary>
    public bool EnableIpv6
    {
        get => _routing.EnableIpv6;
        set { if (_routing.EnableIpv6 != value) { _routing.EnableIpv6 = value; OnPropertyChanged(); SaveRouting(); } }
    }

    // --- DNS resolver -----------------------------------------------------

    /// <summary>A catalog entry plus its last measured latency, shown in the picker.</summary>
    public sealed class DnsProviderItem : ObservableObject
    {
        private string _suffix = "";

        public DnsProviderItem(DnsProvider provider) => Provider = provider;

        public DnsProvider Provider { get; }
        public string Id => Provider.Id;

        /// <summary>Provider name, plus the probe result once one is known.</summary>
        public string Label => Provider.Label + _suffix;

        public void SetResult(int? latencyMs)
        {
            _suffix = " — " + (latencyMs is int ms
                ? L.Format("dns.latency", ms)
                : L["dns.unavailable"]);
            OnPropertyChanged(nameof(Label));
        }

        public void ClearResult()
        {
            _suffix = "";
            OnPropertyChanged(nameof(Label));
        }
    }

    public ObservableCollection<DnsProviderItem> DnsProviders { get; } = new();

    /// <summary>Resolver used for traffic that bypasses the tunnel; the location-sensitive one.</summary>
    public string SelectedDnsProviderId
    {
        get => _routing.Dns.LocalProviderId ?? DnsCatalog.FallbackId;
        set
        {
            if (value is null || _routing.Dns.LocalProviderId == value) return;
            _routing.Dns.LocalProviderId = value;
            OnPropertyChanged();
            SaveRouting();
        }
    }

    /// <summary>
    /// Measures every catalog resolver from this machine. Which resolver is fastest
    /// depends on where the user is, so it can only be answered by measuring here.
    /// </summary>
    private async Task CheckDnsAsync()
    {
        StatusText = L["dns.checking"];
        foreach (var item in DnsProviders)
            item.ClearResult();

        var results = await Task.WhenAll(DnsProviders.Select(async item =>
            (item, latency: await ProbeAsync(item.Provider))));

        foreach (var (item, latency) in results)
            item.SetResult(latency);

        var best = results.Where(r => r.latency is not null)
                          .OrderBy(r => r.latency)
                          .Select(r => r.item)
                          .FirstOrDefault();
        StatusText = best is null
            ? L["dns.noneReachable"]
            : L.Format("dns.fastest", best.Provider.Label);
    }

    /// <summary>Measures a resolver with the probe that matches its transport.</summary>
    private static Task<int?> ProbeAsync(DnsProvider provider) =>
        provider.Transport == DnsTransport.Udp
            ? UdpDnsProbe.MeasureAsync(provider.Server)
            : DohProbe.MeasureAsync(provider.Server);

    /// <summary>
    /// Called once after the window loads: connects if auto-connect is on, or if
    /// <paramref name="force"/> is set (the elevated relaunch after a Connect click),
    /// and a target exists.
    /// </summary>
    public async Task TryAutoConnectOnStartupAsync(bool force = false)
    {
        if ((force || AutoConnect) && !IsConnected && ResolveConnectTargets().Count > 0)
            await ToggleConnectionAsync();
    }

    private static bool ToggleTag(List<string> list, string tag, bool present)
    {
        if (present)
        {
            if (list.Contains(tag)) return false;
            list.Add(tag);
            return true;
        }
        return list.Remove(tag);
    }

    private void SaveRouting()
    {
        try { _settingsStore.Save(_routing); }
        catch (Exception ex) { StatusText = L.Format("status.saveSettingsFail", ex.Message); }
    }

    private void SaveProfiles()
    {
        try { _store.Save(Profiles); }
        catch (Exception ex) { StatusText = L.Format("status.saveProfilesFail", ex.Message); }
    }

    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            try { await _connection.DisconnectAsync(); }
            catch (Exception ex) { StatusText = L.Format("status.disconnectError", ex.Message); }
            return;
        }

        var targets = ResolveConnectTargets();
        if (targets.Count == 0)
        {
            StatusText = L["status.selectProfile"];
            return;
        }

        // Remember a single-server target so the elevated relaunch (and the next launch)
        // reconnects to it, not the first profile. Save before the relaunch below.
        if (targets.Count == 1 && _routing.LastProfileTag != targets[0].Tag)
        {
            _routing.LastProfileTag = targets[0].Tag;
            SaveRouting();
        }

        if (!File.Exists(AppPaths.CoreExe))
        {
            StatusText = L.Format("status.coreNotFound", AppPaths.CoreExe);
            return;
        }

        if (!Elevation.IsElevated())
        {
            StatusText = L["status.needAdmin"];
            // Relaunch elevated and ask that instance to connect automatically, so the
            // user doesn't have to click Connect a second time after the UAC prompt.
            if (!Elevation.RelaunchElevated("--connect"))
                StatusText = L["status.adminCancelled"];
            else
                Application.Current.Shutdown();
            return;
        }

        try
        {
            _activeLabel = targets.Count > 1
                ? L.Format("active.group", targets.Count,
                    L[SelectedFailoverMode == FailoverMode.UrlTest ? "active.auto" : "active.manual"])
                : targets[0].Tag;
            StatusText = L["status.connecting"];
            var options = new GeneratorOptions { RuleSetDirectory = AppPaths.GeoDir };
            var configJson = SingBoxConfigGenerator.GenerateJson(targets, _routing, options);
            await _connection.ConnectAsync(configJson, options.TunInterfaceName, AppPaths.CoreExe, _routing.KillSwitch);
        }
        catch (OperationCanceledException)
        {
            // Connect was cancelled by a disconnect/shutdown — not an error.
        }
        catch (Exception ex)
        {
            StatusText = L.Format("status.error", ex.Message);
        }
    }

    /// <summary>Servers to connect: the failover group if ≥2 are marked, else the selected one.</summary>
    private List<ProfileConfig> ResolveConnectTargets()
    {
        var group = Profiles.Where(p => p.InGroup).ToList();
        if (group.Count >= 2) return group;
        if (SelectedProfile is not null) return new List<ProfileConfig> { SelectedProfile };
        return new List<ProfileConfig>();
    }

    // Acquisition lives in ImportCoordinator; the view-model only maps the outcome to a status
    // and, on success, parses/dedups (ProfileImporter) and merges into the list.
    private void ImportFromClipboard() => ApplyImport(_import.FromClipboard(), "status.noLinksClip");
    private void ImportFromFile() => ApplyImport(_import.FromFile(), "status.noLinksFile");
    private void ImportQrFromFile() => ApplyImport(_import.FromQrFile(), "status.noLinksQr");
    private void ImportQrFromClipboard() => ApplyImport(_import.FromQrClipboard(), "status.noLinksQr");
    private void ImportQrFromWebcam() => ApplyImport(_import.FromWebcam(), "status.noLinksQr");

    /// <summary>Maps an acquisition outcome to a status; on Ok, parses and adds the profiles.</summary>
    private void ApplyImport(ImportOutcome outcome, string noneFoundKey)
    {
        switch (outcome.Status)
        {
            case ImportStatus.Cancelled: return;
            case ImportStatus.ClipboardReadFail: StatusText = L["status.clipReadFail"]; return;
            case ImportStatus.FileReadFail: StatusText = L.Format("status.fileReadFail", outcome.Detail ?? ""); return;
            case ImportStatus.ImageReadFail: StatusText = L.Format("status.imgReadFail", outcome.Detail ?? ""); return;
            case ImportStatus.ImageClipboardReadFail: StatusText = L["status.imgClipReadFail"]; return;
            case ImportStatus.NoImage: StatusText = L["status.noImgClip"]; return;
            case ImportStatus.QrError: StatusText = L.Format("status.qrError", outcome.Detail ?? ""); return;
            case ImportStatus.QrNotRecognized: StatusText = L["status.qrNotRecognized"]; return;
            case ImportStatus.Ok: AddParsedProfiles(outcome.Text ?? "", noneFoundKey); return;
            default: throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Status, "Unhandled import status");
        }
    }

    /// <summary>Parses share-links from <paramref name="text"/>, adds new (deduped) ones, persists.</summary>
    private void AddParsedProfiles(string text, string noneFoundKey)
    {
        var result = ProfileImporter.Import(text, Profiles);
        if (!result.AnyParsed)
        {
            StatusText = L[noneFoundKey];
            return;
        }

        foreach (var p in result.New)
            Profiles.Add(p);

        SelectedProfile ??= Profiles.FirstOrDefault();
        SaveProfiles();
        StatusText = result.New.Count > 0
            ? L.Format("status.profilesAdded", result.New.Count)
            : L["status.profilesExist"];
    }

    private void RemoveSelected()
    {
        if (SelectedProfile is null)
            return;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        SaveProfiles();
    }

    private void RenameSelected()
    {
        if (SelectedProfile is null)
            return;
        var dlg = new TextInputWindow(L["rename.title"], L["rename.prompt"], SelectedProfile.Tag)
        { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
            return;

        SelectedProfile.Tag = dlg.Value;
        ReplaceProfiles(Profiles.ToList()); // ProfileConfig isn't observable — reset to refresh the list
        SaveProfiles();
    }

    private async Task PingAllAsync()
    {
        if (Profiles.Count == 0)
            return;

        StatusText = L["status.pinging"];
        var list = Profiles.ToList();
        await Task.WhenAll(list.Select(async p =>
        {
            p.LatencyMs = await LatencyProbe.MeasureAsync(p.Server, p.Port);
            // Always leave a visible result so a ping renders even when disconnected:
            // "<n> ms" when reachable, a dash when the direct probe couldn't connect.
            p.LatencyText = p.LatencyMs is int ms ? $"{ms} ms" : "—";
        }));

        // Reorder the CURRENT list (not the pre-await snapshot) so a profile removed or imported
        // during the probe isn't resurrected or dropped. Materialize before ReplaceProfiles clears.
        // Reachable/fastest first; unreachable (null) last, then by name.
        ReplaceProfiles(Profiles
            .OrderBy(p => p.LatencyMs ?? int.MaxValue)
            .ThenBy(p => p.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList());
        SaveProfiles();
        StatusText = L["status.pingDone"];

        // When connected, the ping message replaced the "Connected: <server>" status.
        // Restore it after a short delay so the user can still see what they're connected to.
        if (IsConnected)
            _ = RestoreConnectedStatusAsync(L["status.pingDone"]);
    }

    /// <summary>Restores the "connected to" label after a transient status, if still connected and unchanged.</summary>
    private async Task RestoreConnectedStatusAsync(string transientMessage)
    {
        await Task.Delay(3000);
        if (IsConnected && StatusText == transientMessage)
            StatusText = L.Format("status.connectedTo", _activeLabel);
    }

    /// <summary>Replaces the profile list content (used to refresh non-observable items / reorder).</summary>
    private void ReplaceProfiles(IEnumerable<ProfileConfig> ordered)
    {
        var sel = SelectedProfile;
        Profiles.Clear();
        foreach (var p in ordered)
            Profiles.Add(p);
        SelectedProfile = sel is not null && Profiles.Contains(sel) ? sel : Profiles.FirstOrDefault();
    }

    // Engine events arrive on thread-pool threads. BeginInvoke (not Invoke): a blocking
    // dispatch would deadlock the synchronous engine shutdown in MainWindow.Closed.
    private void OnEngineStateChanged(object? sender, EngineState state) =>
        _dispatcher.BeginInvoke(() =>
        {
            IsConnected = state == EngineState.Running;
            StatusText = state switch
            {
                EngineState.Running => L.Format("status.connectedTo", _activeLabel),
                EngineState.Starting => L["status.connecting"],
                EngineState.Stopping => L["status.disconnecting"],
                EngineState.Faulted => L["status.faulted"],
                _ => L["status.disconnected"],
            };
            _engineState = state;
            OnPropertyChanged(nameof(TrayIcon));
            OnPropertyChanged(nameof(TrayToolTip));
            UpdateActiveProfiles(state == EngineState.Running);
        });

    /// <summary>Marks the connected server(s) so the list can highlight them; clears when down.</summary>
    private void UpdateActiveProfiles(bool connected)
    {
        var active = connected ? ResolveConnectTargets() : new List<ProfileConfig>();
        foreach (var p in Profiles)
            p.IsActive = active.Contains(p);
    }

    // --- Tray status indicator -------------------------------------------

    private EngineState _engineState = EngineState.Stopped;

    private static readonly System.Windows.Media.ImageSource TrayOff = TrayImage("tray-off");
    private static readonly System.Windows.Media.ImageSource TrayConnecting = TrayImage("tray-connecting");
    private static readonly System.Windows.Media.ImageSource TrayOn = TrayImage("tray-on");
    private static readonly System.Windows.Media.ImageSource TrayFault = TrayImage("tray-fault");

    private static System.Windows.Media.ImageSource TrayImage(string name)
    {
        var img = new System.Windows.Media.Imaging.BitmapImage(
            new Uri($"pack://application:,,,/Resources/{name}.ico"));
        img.Freeze(); // shared across threads / reused
        return img;
    }

    /// <summary>Tray icon reflecting the engine state (grey/amber/green/red).</summary>
    public System.Windows.Media.ImageSource TrayIcon => _engineState switch
    {
        EngineState.Running => TrayOn,
        EngineState.Starting or EngineState.Stopping => TrayConnecting,
        EngineState.Faulted => TrayFault,
        _ => TrayOff,
    };

    /// <summary>Tray tooltip: product name plus the current status line.</summary>
    public string TrayToolTip => "SimpleRay — " + StatusText;

    private void OnEngineLog(object? sender, string line) =>
        _dispatcher.BeginInvoke(() =>
        {
            var combined = Log + line + Environment.NewLine;
            if (combined.Length > MaxLogChars)
                combined = combined[^MaxLogChars..];
            Log = combined;
        });

    /// <summary>
    /// Stops the engine. Deliberately not an async method: MainWindow.Closed blocks on
    /// this task, so capturing the dispatcher context here would post the continuation
    /// to a UI thread that is already blocked — deadlocking the app on exit. The engine
    /// uses ConfigureAwait(false) throughout, so the returned task completes off the UI thread.
    /// </summary>
    public Task ShutdownAsync() => _connection.ShutdownAsync();
}
