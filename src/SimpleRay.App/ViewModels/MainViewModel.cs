using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
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
    private readonly DispatcherTimer _updateTimer;
    private bool _subscriptionsBusy;
    private readonly ConnectionController _connection;
    private readonly RoutingSettings _routing;
    private readonly Dispatcher _dispatcher;
    private readonly IDialogService _dialogs;
    private readonly ImportCoordinator _import = new();
    private readonly ProxyProbe _proxyProbe = new();

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
        ImportClipboardCommand = new RelayCommand(ImportFromClipboardAsync);
        ImportFileCommand = new RelayCommand(ImportFromFileAsync);
        ImportQrFileCommand = new RelayCommand(ImportQrFromFileAsync);
        ImportQrClipboardCommand = new RelayCommand(ImportQrFromClipboardAsync);
        ImportQrWebcamCommand = new RelayCommand(ImportQrFromWebcamAsync);
        ImportManualCommand = new RelayCommand(ImportManualAsync);
        RemoveCommand = new RelayCommand(RemoveSelected, CanRenameOrRemove);
        RenameCommand = new RelayCommand(RenameSelectedAsync, CanRenameOrRemove);
        PingCommand = new RelayCommand(PingAllAsync, () => Profiles.Count > 0);
        PingProxyCommand = new RelayCommand(PingThroughServersAsync, () => Profiles.Count > 0);
        SpeedTestCommand = new RelayCommand(SpeedTestAsync, () => SelectedProfile is not null);
        OpenAppsCommand = new RelayCommand(OpenAppRouting);
        DirectDomainsCommand = new RelayCommand(EditDirectDomains);
        ProxyDomainsCommand = new RelayCommand(EditProxyDomains);
        GroupChangedCommand = new RelayCommand(OnGroupChanged);
        CheckUpdatesCommand = new RelayCommand(CheckForUpdatesAsync);
        CheckDnsCommand = new RelayCommand(CheckDnsAsync);
        RefreshSubscriptionsCommand = new RelayCommand(RefreshSubscriptionsAsync,
            () => _subscriptions.Count > 0);
        ToggleSubscriptionsPanelCommand = new RelayCommand(() => IsSubscriptionsPanelOpen = !IsSubscriptionsPanelOpen);

        ServersView = new ListCollectionView(Profiles)
        {
            Filter = o => SelectedSource is not { } source || (o is ProfileConfig p && source.Matches(p)),
        };
        RebuildSources();
        OpenSupportCommand = new RelayCommand(() => UrlLauncher.TryOpen(SubscriptionSupportUrl));
        OpenWebPageCommand = new RelayCommand(() => UrlLauncher.TryOpen(SubscriptionWebPageUrl));

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

        // Background update check: once shortly after launch, then every 6 hours. It only lights
        // up the footer link; nothing is downloaded without the user's consent.
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(20),
        };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckForUpdatesInBackgroundAsync();
        };
        _updateTimer.Start();

        L.CultureChanged += OnCultureChanged;
        Profiles.CollectionChanged += (_, _) =>
        {
            PingCommand.RaiseCanExecuteChanged();
            PingProxyCommand.RaiseCanExecuteChanged();
            // Adding/removing a server can change the in-group count, so the hint that
            // reports it must refresh too (e.g. removing a grouped server).
            OnPropertyChanged(nameof(GroupHint));
            RebuildSources(); // per-source counts
        };
    }

    // --- Localization -----------------------------------------------------

    public IReadOnlyList<LanguageOption> Languages => L.Languages;

    public string LanguageShort => SelectedLanguage.ToUpperInvariant();

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
        foreach (var mode in Modes)
            mode.RefreshLabel();
        OnPropertyChanged(nameof(GroupHint));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(LanguageShort));
        OnPropertyChanged(nameof(UpdateCheckStatus));
        OnPropertyChanged(nameof(UpdateLinkText));
        RaiseSubscriptionInfoChanged();
        // Probe results carry localized text ("unavailable", units); drop them rather
        // than leave the picker mixing two languages.
        foreach (var item in DnsProviders)
            item.ClearResult();
        if (!IsConnected)
            StatusText = L["status.disconnected"];
    }

    public string WindowTitle => "SimpleRay " + UpdateService.CurrentVersion.ToString(3);

    public string AppVersion => "v" + UpdateService.CurrentVersion.ToString(3);

    /// <summary>Public source repository — surfaced in About so users can obtain the source (GPL).</summary>
    public const string SourceUrl = "https://github.com/OlBaskov-rec/SimpleRay";

    public string AboutTitle => L["about.title"];

    public string TrayHintTitle => L["tray.hintTitle"];
    public string TrayHintText => L["tray.hintText"];

    /// <summary>True only the first time the window is closed to the tray (then remembered).</summary>
    public bool TakeFirstTrayHint()
    {
        if (_routing.TrayHintShown)
            return false;
        _routing.TrayHintShown = true;
        SaveRouting();
        return true;
    }

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
                SpeedTestCommand.RaiseCanExecuteChanged();
                RaiseSubscriptionInfoChanged();
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
    public RelayCommand PingProxyCommand { get; }
    public RelayCommand SpeedTestCommand { get; }

    /// <summary>Speed test target and limits: a neutral download endpoint, capped in size and time.</summary>
    private const string SpeedTestUrl = "https://speed.cloudflare.com/__down?bytes=10000000";
    private const long SpeedTestMaxBytes = 10_000_000;
    private static readonly TimeSpan SpeedTestMaxTime = TimeSpan.FromSeconds(10);
    public RelayCommand OpenAppsCommand { get; }
    public RelayCommand DirectDomainsCommand { get; }
    public RelayCommand ProxyDomainsCommand { get; }
    public RelayCommand GroupChangedCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand CheckDnsCommand { get; }
    public RelayCommand ImportManualCommand { get; }
    public RelayCommand RefreshSubscriptionsCommand { get; }
    public RelayCommand ToggleSubscriptionsPanelCommand { get; }

    // --- Subscriptions panel and the server list it filters --------------------------

    /// <summary>Rows of the subscriptions panel: all servers, manual ones, each subscription.</summary>
    public ObservableCollection<ServerSource> Sources { get; } = new();

    /// <summary>The server list as shown: <see cref="Profiles"/> filtered by <see cref="SelectedSource"/>.</summary>
    public ICollectionView ServersView { get; }

    private ServerSource? _selectedSource;
    private bool _rebuildingSources;
    private bool _sourcesListActive; // which list Rename/Remove act on: the panel or the servers

    public ServerSource? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (!SetField(ref _selectedSource, value) || _rebuildingSources)
                return;
            ApplySourceFilter();
        }
    }

    /// <summary>"Servers — &lt;source&gt; (n)" above the server list.</summary>
    public string ServersHeader =>
        L.Format("servers.header", SelectedSource?.Title ?? L["sources.all"], ServersView.Cast<object>().Count());

    public bool IsSubscriptionsPanelOpen
    {
        get => _routing.SubscriptionsPanelOpen;
        set
        {
            if (_routing.SubscriptionsPanelOpen == value) return;
            _routing.SubscriptionsPanelOpen = value;
            SaveRouting();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubscriptionsToggleArrow));
        }
    }

    /// <summary>Arrow on the panel's toggle, pointing where the panel will move (◀ collapse, ▶ expand).</summary>
    public string SubscriptionsToggleArrow => IsSubscriptionsPanelOpen ? "◀" : "▶";

    /// <summary>The window tells which list was clicked last, so Rename/Remove act on that one.</summary>
    public void SetActiveList(bool sources)
    {
        _sourcesListActive = sources;
        RenameCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
    }

    private SubscriptionEntry? ActiveSubscription =>
        _sourcesListActive && SelectedSource is { Kind: ServerSourceKind.Subscription, Entry: { } e } ? e : null;

    private bool CanRenameOrRemove() =>
        _sourcesListActive ? ActiveSubscription is not null : SelectedProfile is not null;

    private void ApplySourceFilter()
    {
        ServersView.Refresh();
        // Keep the selection on a visible server, so what you see is what Connect uses.
        if (SelectedProfile is null || !ServersView.Contains(SelectedProfile))
            SelectedProfile = ServersView.Cast<ProfileConfig>().FirstOrDefault() ?? SelectedProfile;
        OnPropertyChanged(nameof(ServersHeader));
        RenameCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Rebuilds the panel rows (counts, names, metadata), keeping the selected row.</summary>
    private void RebuildSources()
    {
        var key = SelectedSource?.Key;
        _rebuildingSources = true;
        try
        {
            Sources.Clear();
            foreach (var s in ServerSource.Build(_subscriptions, Profiles))
                Sources.Add(s);
            SelectedSource = Sources.FirstOrDefault(s => s.Key == key) ?? Sources.FirstOrDefault();
        }
        finally
        {
            _rebuildingSources = false;
        }
        ServersView.Refresh();
        OnPropertyChanged(nameof(ServersHeader));
        RenameCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
    }

    private void ShowSubscription(string url)
    {
        IsSubscriptionsPanelOpen = true;
        if (Sources.FirstOrDefault(s => s.Entry?.Url == url) is { } source)
            SelectedSource = source;
    }

    /// <summary>
    /// Asks for the optional per-subscription User-Agent, for providers that serve servers only
    /// to the client they issued the link for. False when the user cancelled.
    /// </summary>
    private bool TryPromptUserAgent(string? current, out string? userAgent)
    {
        userAgent = current;
        var dlg = new TextInputWindow(L["sub.promptTitle"],
            L.Format("sub.uaPrompt", SubscriptionService.DefaultUserAgent), current ?? "")
        { Owner = Application.Current?.MainWindow };
        dlg.WithHint(L["sub.supportedTitle"], L["tip.subscriptionAdd"]);
        if (dlg.ShowDialog() != true) return false;
        userAgent = SubscriptionService.NormalizeUserAgent(dlg.Value);
        return true;
    }

    /// <summary>Renames a subscription and/or changes its User-Agent (refetching when that changed).</summary>
    private async Task EditSubscriptionAsync(SubscriptionEntry entry)
    {
        var nameDlg = new TextInputWindow(L["subs.title"], L["subs.renamePrompt"], entry.DisplayName)
        { Owner = Application.Current?.MainWindow };
        if (nameDlg.ShowDialog() != true) return;
        if (!TryPromptUserAgent(entry.UserAgent, out var userAgent)) return;

        var name = nameDlg.Value;
        // Typing the provider's own title back (or nothing) means "follow the provider".
        entry.Name = name.Length == 0 || name == entry.Info.Title ? null : name;
        bool uaChanged = userAgent != entry.UserAgent;
        entry.UserAgent = userAgent;
        SaveSubscriptions();
        RaiseSubscriptionInfoChanged();
        RebuildSources();

        if (uaChanged)
            await RunSubscriptionUpdateAsync(new[] { (entry.Url, entry.UserAgent) }, quiet: false);
    }

    /// <summary>Removes a subscription together with the servers it provided, after confirmation.</summary>
    private void RemoveSubscription(SubscriptionEntry entry)
    {
        var servers = Profiles.Where(p => p.Subscription == entry.Url).ToList();
        if (!_dialogs.Confirm(L.Format("subs.removeConfirm", entry.DisplayName, servers.Count), L["subs.removeTitle"]))
            return;

        foreach (var p in servers)
            Profiles.Remove(p);
        if (SelectedProfile is null || !Profiles.Contains(SelectedProfile))
            SelectedProfile = Profiles.FirstOrDefault();
        SaveProfiles();

        _subscriptions.Remove(entry);
        SaveSubscriptions();
        RefreshSubscriptionsCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(GroupHint));
        RaiseSubscriptionInfoChanged();
        RebuildSources();
        StatusText = L.Format("subs.removed", entry.DisplayName);
    }

    /// <summary>Refreshes the subscription selected in the panel, or all of them.</summary>
    private async Task RefreshSubscriptionsAsync()
    {
        if (_subscriptions.Count == 0)
        {
            StatusText = L["sub.none"];
            return;
        }
        var entries = SelectedSource is { Kind: ServerSourceKind.Subscription, Entry: { } e }
            ? new List<SubscriptionEntry> { e }
            : _subscriptions.ToList();
        await RunSubscriptionUpdateAsync(entries.Select(s => (s.Url, s.UserAgent)).ToList(), quiet: false);
    }

    private void SaveSubscriptions()
    {
        try { _subscriptionStore.Save(_subscriptions); }
        catch (Exception ex) { StatusText = L.Format("status.saveSettingsFail", ex.Message); }
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
            if (content.Metadata.Notices is { Count: > 0 } notices)
            {
                // Only placeholder "servers" (e.g. "App not supported"): still save the
                // subscription and its message, so the user can fix its User-Agent in the
                // subscriptions window — but leave the server list alone.
                var noticeEntry = UpsertSubscription(url, userAgent, content.Metadata);
                if (!quiet)
                    StatusText = L.Format("sub.onlyNotice", noticeEntry.DisplayName, string.Join(" · ", notices));
                return;
            }
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

        var entry = UpsertSubscription(url, userAgent, content.Metadata);
        OnPropertyChanged(nameof(GroupHint));
        if (!quiet)
        {
            StatusText = entry.DisplayName + ": " + (content.Skipped > 0
                ? L.Format("sub.updatedSkipped", fetched.Count, content.Skipped)
                : L.Format("sub.updated", fetched.Count));
        }
    }

    /// <summary>Saves the fetched metadata on the subscription (adding it when new) and notifies.</summary>
    private SubscriptionEntry UpsertSubscription(string url, string? userAgent, SubscriptionMetadata metadata)
    {
        var entry = _subscriptions.FirstOrDefault(s => s.Url == url);
        if (entry is null)
        {
            entry = new SubscriptionEntry { Url = url, UserAgent = userAgent };
            _subscriptions.Add(entry);
            RefreshSubscriptionsCommand.RaiseCanExecuteChanged();
        }
        entry.Info = metadata;
        entry.LastUpdated = DateTimeOffset.Now;
        SaveSubscriptions();
        RaiseSubscriptionInfoChanged();
        RebuildSources();
        return entry;
    }

    /// <summary>
    /// Provider info for the selected server's subscription: name, traffic used/quota, expiry
    /// and announcement, one per line; null when the server isn't from a subscription.
    /// </summary>
    public string? SubscriptionInfo
    {
        get
        {
            var entry = SelectedSubscription;
            if (entry is null)
                return null;
            var lines = SubscriptionText.InfoLines(entry.Info, includeTitle: false, includeAnnounce: true).Prepend(entry.DisplayName);
            return string.Join("\n", lines);
        }
    }

    /// <summary>The subscription the selected server came from, if any.</summary>
    private SubscriptionEntry? SelectedSubscription =>
        SelectedProfile?.Subscription is { } url ? _subscriptions.FirstOrDefault(s => s.Url == url) : null;

    /// <summary>The provider's support / web page links for the selected server (null hides the link).</summary>
    public string? SubscriptionSupportUrl => SelectedSubscription?.Info.SupportUrl;
    public string? SubscriptionWebPageUrl => SelectedSubscription?.Info.WebPageUrl;

    public RelayCommand OpenSupportCommand { get; }
    public RelayCommand OpenWebPageCommand { get; }

    private void RaiseSubscriptionInfoChanged()
    {
        OnPropertyChanged(nameof(SubscriptionInfo));
        OnPropertyChanged(nameof(SubscriptionSupportUrl));
        OnPropertyChanged(nameof(SubscriptionWebPageUrl));
    }

    private string? _availableUpdate;

    /// <summary>A newer release was found by the background check (the info button turns green and blinks).</summary>
    public bool UpdateAvailable => _availableUpdate is not null;

    /// <summary>Update button: "Updates", or "Update 1.2.3" once a newer release is known.</summary>
    public string UpdateLinkText => _availableUpdate is { } v ? L.Format("updates.available", v) : L["updates.check"];

    private string? _updateStatusKey;
    private object[] _updateStatusArgs = Array.Empty<object>();
    public string UpdateCheckStatus => _updateStatusKey is null ? "" : L.Format(_updateStatusKey, _updateStatusArgs);

    private void SetUpdateStatus(string key, params object[] args)
    {
        _updateStatusKey = key;
        _updateStatusArgs = args;
        OnPropertyChanged(nameof(UpdateCheckStatus));
        StatusText = UpdateCheckStatus;
    }

    private void SetAvailableUpdate(string? version)
    {
        if (_availableUpdate == version) return;
        _availableUpdate = version;
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(UpdateLinkText));
    }

    /// <summary>
    /// Quiet periodic check (no dialogs, failures ignored): only flags the info button. Installing
    /// still goes through the normal consent flow when the user clicks it.
    /// </summary>
    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var info = await _updateService.CheckAsync();
            var version = info?.Version.ToString(3);
            SetAvailableUpdate(version);
        }
        catch (Exception)
        {
            // Offline, GitHub unreachable, rate-limited: try again on the next tick.
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (IsConnected)
        {
            SetUpdateStatus("status.updateConnected");
            return;
        }

        SetUpdateStatus("status.updateChecking");
        ReleaseInfo? info;
        try
        {
            info = await _updateService.CheckAsync();
        }
        catch (Exception ex)
        {
            SetUpdateStatus("status.updateCheckFail", ex.Message);
            return;
        }

        SetAvailableUpdate(info?.Version.ToString(3));

        if (info is null)
        {
            SetUpdateStatus("status.updateLatest", AppVersion);
            return;
        }

        var consent = _dialogs.Confirm(
            L.Format("update.consent", info.Version.ToString(3), UpdateService.CurrentVersion.ToString(3)),
            L["update.consentTitle"]);
        if (!consent)
        {
            SetUpdateStatus("status.updateDeferred");
            return;
        }

        try
        {
            var progress = new Progress<double>(p => SetUpdateStatus("status.updateDownloading", p.ToString("P0")));
            var staging = await _updateService.DownloadAndStageAsync(info, progress);

            SetUpdateStatus("status.updateApplying");
            await _connection.ShutdownAsync(); // stop sing-box so its files aren't locked
            _updateService.ApplyAndRestart(staging);
            App.Quit();
        }
        catch (Exception ex)
        {
            SetUpdateStatus("status.updateError", ex.Message);
        }
    }

    // --- Failover group ---------------------------------------------------

    /// <summary>
    /// Auto-switch between the servers marked "in group" (sing-box urltest). Off: connect only
    /// the selected server. Replaces the old urltest/selector choice — the app never offered a
    /// way to pick inside a selector, so "manual" just meant "the first grouped server".
    /// </summary>
    public bool AutoSwitch
    {
        get => _routing.Failover.Mode == FailoverMode.UrlTest;
        set
        {
            var mode = value ? FailoverMode.UrlTest : FailoverMode.Selector;
            if (_routing.Failover.Mode == mode) return;
            _routing.Failover.Mode = mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupHint));
            SaveRouting();
        }
    }

    /// <summary>Hint about the current failover group composition.</summary>
    public string GroupHint
    {
        get
        {
            if (!AutoSwitch)
                return L["group.hintOff"];
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

    /// <summary>A routing-mode choice whose label follows the UI language.</summary>
    public sealed class RoutingModeItem : ObservableObject
    {
        private readonly string _labelKey;

        public RoutingModeItem(string labelKey, RoutingMode mode)
        {
            _labelKey = labelKey;
            Mode = mode;
        }

        public RoutingMode Mode { get; }
        public string Label => L[_labelKey];

        /// <summary>Re-reads the label after a language switch (the item itself stays).</summary>
        public void RefreshLabel() => OnPropertyChanged(nameof(Label));
    }

    /// <summary>The mode choices, created once.</summary>
    public static IReadOnlyList<RoutingModeItem> CreateModeItems() => new[]
    {
        new RoutingModeItem(LocKeys.ModeGlobal, RoutingMode.Global),
        new RoutingModeItem(LocKeys.ModeRule, RoutingMode.Rule),
        new RoutingModeItem(LocKeys.ModeDirect, RoutingMode.Direct),
    };

    // The SAME items for the window's lifetime, relabelled in place on a language switch.
    // Rebuilding the list made the ComboBox drop its selection and push null into
    // SelectedMode (a non-nullable enum): the mode box went blank with a red error border.
    public IReadOnlyList<RoutingModeItem> Modes { get; } = CreateModeItems();

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
                App.Quit();
            return;
        }

        try
        {
            _activeLabel = targets.Count > 1
                ? L.Format("active.group", targets.Count,
                    L["active.auto"])
                : targets[0].Tag;
            StatusText = L["status.connecting"];
            var options = new GeneratorOptions { RuleSetDirectory = AppPaths.GeoDir };
            string lanDnsLog;
            try
            {
                options.LanDns = LanDnsDetector.Detect(options.TunInterfaceName);
                lanDnsLog = options.LanDns is { } lanDns
                    ? $"LAN DNS: {string.Join(", ", lanDns.Suffixes)} \u2192 {lanDns.Server}"
                    : "LAN DNS: not enabled";
            }
            catch (Exception ex)
            {
                lanDnsLog = $"LAN DNS: not enabled ({ex.Message})";
            }
            OnEngineLog(this, lanDnsLog);
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
        if (AutoSwitch && group.Count >= 2) return group;
        if (SelectedProfile is not null) return new List<ProfileConfig> { SelectedProfile };
        return new List<ProfileConfig>();
    }

    // Acquisition lives in ImportCoordinator; the view-model maps the outcome to a status and,
    // on success, sorts the text (ImportClassifier) into subscriptions and servers.
    private Task ImportFromClipboardAsync() => SmartImportAsync(_import.FromClipboard(), "status.noLinksClip");
    private Task ImportFromFileAsync() => SmartImportAsync(_import.FromFile(), "status.noLinksFile");
    private Task ImportQrFromFileAsync() => SmartImportAsync(_import.FromQrFile(), "status.noLinksQr");
    private Task ImportQrFromClipboardAsync() => SmartImportAsync(_import.FromQrClipboard(), "status.noLinksQr");
    private Task ImportQrFromWebcamAsync() => SmartImportAsync(_import.FromWebcam(), "status.noLinksQr");

    private async Task ImportManualAsync()
    {
        var dlg = new TextInputWindow(L["import.manualTitle"], L["import.manualPrompt"])
        { Owner = Application.Current?.MainWindow };
        dlg.WithHint(L["sub.supportedTitle"], L["tip.subscriptionAdd"]);
        if (dlg.ShowDialog() != true) return;
        await ImportTextAsync(dlg.Value, "status.noLinksText");
    }

    /// <summary>Maps an acquisition outcome to a status; on Ok, imports whatever the text holds.</summary>
    private async Task SmartImportAsync(ImportOutcome outcome, string noneFoundKey)
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
            case ImportStatus.Ok: await ImportTextAsync(outcome.Text ?? "", noneFoundKey); return;
            default: throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Status, "Unhandled import status");
        }
    }

    /// <summary>
    /// One "Add" for every kind of input: server links / .conf / JSON are added (deduped),
    /// subscription URLs and other clients' import links are fetched as subscriptions (the
    /// panel opens on the new one). The status line sums up every part.
    /// </summary>
    private async Task ImportTextAsync(string text, string noneFoundKey)
    {
        var plan = ImportClassifier.Classify(text);
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(plan.ServerText))
        {
            var result = ProfileImporter.Import(plan.ServerText, Profiles);
            if (result.AnyParsed)
            {
                foreach (var p in result.New)
                    Profiles.Add(p);
                if (result.New.Count > 0)
                {
                    // Show the newcomers: they're manual servers, not part of the selected subscription.
                    if (SelectedSource is { Kind: ServerSourceKind.Subscription })
                        SelectedSource = Sources.FirstOrDefault(s => s.Kind == ServerSourceKind.Manual) ?? SelectedSource;
                    SelectedProfile = result.New[0];
                    SaveProfiles();
                }
                parts.Add(result.New.Count > 0
                    ? L.Format("status.profilesAdded", result.New.Count)
                    : L["status.profilesExist"]);
            }
        }

        foreach (var url in plan.SubscriptionUrls)
        {
            var existing = _subscriptions.FirstOrDefault(s => s.Url == url);
            await RunSubscriptionUpdateAsync(new[] { (url, existing?.UserAgent) }, quiet: false);
            parts.Add(StatusText);
            if (_subscriptions.Any(s => s.Url == url))
                ShowSubscription(url);
        }

        if (plan.HappEncrypted)
            parts.Add(L["sub.happEncrypted"]);

        StatusText = parts.Count > 0 ? string.Join(" · ", parts) : L[noneFoundKey];
    }

    private void RemoveSelected()
    {
        if (ActiveSubscription is { } entry)
        {
            RemoveSubscription(entry);
            return;
        }
        if (SelectedProfile is null)
            return;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = ServersView.Cast<ProfileConfig>().FirstOrDefault() ?? Profiles.FirstOrDefault();
        SaveProfiles();
    }

    private async Task RenameSelectedAsync()
    {
        if (ActiveSubscription is { } entry)
        {
            await EditSubscriptionAsync(entry);
            return;
        }
        RenameSelectedServer();
    }

    private void RenameSelectedServer()
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
        var list = ServersView.Cast<ProfileConfig>().ToList(); // what's on screen
        await Task.WhenAll(list.Select(async p =>
        {
            if (p.IsUdpBased)
            {
                // WireGuard / Hysteria2 listen on UDP: a TCP connect would always "fail".
                p.LatencyMs = null;
                p.LatencyText = "UDP";
                return;
            }
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
    /// <summary>
    /// The real check: a request to the auto-switch test URL through each visible server
    /// (ProxyProbe — a throwaway local sing-box, works while disconnected too). Shows a second
    /// figure per server and sorts working servers first, fastest on top.
    /// </summary>
    private async Task PingThroughServersAsync()
    {
        var list = ServersView.Cast<ProfileConfig>().ToList(); // what's on screen
        if (list.Count == 0)
            return;
        if (!File.Exists(AppPaths.CoreExe))
        {
            StatusText = L["status.proxyPingNoCore"];
            return;
        }

        StatusText = L.Format("status.proxyPinging", list.Count);
        foreach (var p in list)
            p.ProxyLatencyText = "…";

        IReadOnlyList<int?> results;
        try
        {
            results = await _proxyProbe.MeasureAsync(list, _routing.Failover.TestUrl);
        }
        catch (Exception ex)
        {
            foreach (var p in list)
                p.ProxyLatencyText = null;
            StatusText = L.Format("status.proxyPingFail", ex.Message);
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            list[i].ProxyLatencyMs = results[i];
            list[i].ProxyLatencyText = results[i] is int ms
                ? L.Format("ping.viaServer", ms)
                : L["ping.viaServerFail"];
        }

        // Working through the server first (fastest on top), then by the direct ping and name.
        ReplaceProfiles(Profiles
            .OrderBy(p => p.ProxyLatencyMs ?? int.MaxValue)
            .ThenBy(p => p.LatencyMs ?? int.MaxValue)
            .ThenBy(p => p.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList());
        SaveProfiles();
        var done = L.Format("status.proxyPingDone", results.Count(r => r is not null), list.Count);
        StatusText = done;
        if (IsConnected)
            _ = RestoreConnectedStatusAsync(done);
    }

    /// <summary>
    /// Download speed through the selected server (on demand only — it spends subscription
    /// traffic): up to 10 MB / 10 s from a neutral endpoint via the same throwaway sing-box as
    /// "ping via server". The result stays next to the server as a third figure.
    /// </summary>
    private async Task SpeedTestAsync()
    {
        var p = SelectedProfile;
        if (p is null)
            return;
        if (!File.Exists(AppPaths.CoreExe))
        {
            StatusText = L["status.proxyPingNoCore"];
            return;
        }

        StatusText = L.Format("status.speedTesting", p.Tag);
        p.SpeedText = "↓ …";
        double? mbps;
        try
        {
            mbps = await _proxyProbe.MeasureSpeedAsync(p, SpeedTestUrl, SpeedTestMaxBytes, SpeedTestMaxTime);
        }
        catch (Exception ex)
        {
            p.SpeedText = null;
            StatusText = L.Format("status.proxyPingFail", ex.Message);
            return;
        }

        p.SpeedMbps = mbps;
        var figure = mbps is double v ? L.Format("speed.value", v.ToString(v < 10 ? "0.0" : "0")) : null;
        p.SpeedText = figure ?? L["speed.fail"];
        var done = figure is not null
            ? L.Format("status.speedDone", p.Tag, figure)
            : L.Format("status.speedFail", p.Tag);
        StatusText = done;
        if (IsConnected)
            _ = RestoreConnectedStatusAsync(done);
    }

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
