using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SimpleRay.App.Engine;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Localization;
using SimpleRay.App.Services;
using SimpleRay.Core.Config;
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
    private readonly List<string> _subscriptions;
    private readonly IVpnEngine _engine;
    private readonly RoutingSettings _routing;
    private readonly Dispatcher _dispatcher;

    // geosite/geoip tags backing the routing presets (must exist as *.srs in GeoDir).
    private const string TagRuSites = "geosite-category-ru";
    private const string TagRuIp = "geoip-ru";
    private const string TagPrivate = "geosite-private";

    private ProfileConfig? _selectedProfile;
    private bool _isConnected;
    private string _statusText;
    private string _log = "";
    private string _activeLabel = "";

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _routing = _settingsStore.Load();
        _subscriptions = _subscriptionStore.Load();
        _statusText = L["status.disconnected"];

        foreach (var p in _store.Load())
            Profiles.Add(p);
        _selectedProfile = Profiles.FirstOrDefault();

        _engine = new SingBoxEngine(new EngineOptions
        {
            ExecutablePath = AppPaths.CoreExe,
            WorkingDirectory = AppPaths.RuntimeDir,
            Terminator = new ConsoleCtrlTerminator(),
        });
        _engine.StateChanged += OnEngineStateChanged;
        _engine.LogReceived += OnEngineLog;

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
        GroupChangedCommand = new RelayCommand(OnGroupChanged);
        CheckUpdatesCommand = new RelayCommand(CheckForUpdatesAsync);
        AddSubscriptionCommand = new RelayCommand(AddSubscriptionAsync);
        RefreshSubscriptionsCommand = new RelayCommand(RefreshSubscriptionsAsync,
            () => _subscriptions.Count > 0);

        L.CultureChanged += OnCultureChanged;
        Profiles.CollectionChanged += (_, _) => PingCommand.RaiseCanExecuteChanged();
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
        if (!IsConnected)
            StatusText = L["status.disconnected"];
    }

    public string AppVersion => "v" + UpdateService.CurrentVersion.ToString(3);

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
        private set => SetField(ref _statusText, value);
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
    public RelayCommand GroupChangedCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand AddSubscriptionCommand { get; }
    public RelayCommand RefreshSubscriptionsCommand { get; }

    private async Task AddSubscriptionAsync()
    {
        var dlg = new TextInputWindow(L["sub.promptTitle"], L["sub.promptText"],
            _subscriptions.LastOrDefault() ?? "")
        { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var url = dlg.Value;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            StatusText = L["sub.badUrl"];
            return;
        }
        await ImportSubscriptionAsync(url);
    }

    private async Task RefreshSubscriptionsAsync()
    {
        if (_subscriptions.Count == 0)
        {
            StatusText = L["sub.none"];
            return;
        }
        foreach (var url in _subscriptions.ToList())
            await ImportSubscriptionAsync(url);
    }

    /// <summary>Fetches a subscription and replaces the profiles that came from it (group membership preserved).</summary>
    private async Task ImportSubscriptionAsync(string url)
    {
        StatusText = L["sub.loading"];
        IReadOnlyList<ProfileConfig> fetched;
        try
        {
            fetched = await _subscriptionService.FetchAsync(url);
        }
        catch (Exception ex)
        {
            StatusText = L.Format("sub.loadFail", ex.Message);
            return;
        }

        if (fetched.Count == 0)
        {
            StatusText = L["sub.empty"];
            return;
        }

        // Preserve group membership across a refresh (match by Raw).
        var inGroup = Profiles.Where(p => p.InGroup && p.Raw is not null)
            .Select(p => p.Raw!).ToHashSet();

        foreach (var stale in Profiles.Where(p => p.Subscription == url).ToList())
            Profiles.Remove(stale);

        foreach (var p in fetched)
        {
            if (p.Raw is not null && inGroup.Contains(p.Raw))
                p.InGroup = true;
            Profiles.Add(p);
        }

        SelectedProfile ??= Profiles.FirstOrDefault();
        SaveProfiles();

        if (!_subscriptions.Contains(url))
        {
            _subscriptions.Add(url);
            _subscriptionStore.Save(_subscriptions);
            RefreshSubscriptionsCommand.RaiseCanExecuteChanged();
        }

        OnPropertyChanged(nameof(GroupHint));
        StatusText = L.Format("sub.updated", fetched.Count);
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

        var consent = MessageBox.Show(
            L.Format("update.consent", info.Version.ToString(3), UpdateService.CurrentVersion.ToString(3)),
            L["update.consentTitle"], MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (consent != MessageBoxResult.Yes)
        {
            StatusText = L["status.updateDeferred"];
            return;
        }

        try
        {
            var progress = new Progress<double>(p => StatusText = L.Format("status.updateDownloading", p.ToString("P0")));
            var staging = await _updateService.DownloadAndStageAsync(info, progress);

            StatusText = L["status.updateApplying"];
            await _engine.DisposeAsync(); // stop sing-box so its files aren't locked
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
            try { await _engine.StopAsync(); }
            catch (Exception ex) { StatusText = L.Format("status.disconnectError", ex.Message); }
            return;
        }

        var targets = ResolveConnectTargets();
        if (targets.Count == 0)
        {
            StatusText = L["status.selectProfile"];
            return;
        }

        if (!File.Exists(AppPaths.CoreExe))
        {
            StatusText = L.Format("status.coreNotFound", AppPaths.CoreExe);
            return;
        }

        if (!Elevation.IsElevated())
        {
            StatusText = L["status.needAdmin"];
            if (!Elevation.RelaunchElevated())
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
            await _engine.StartAsync(configJson);
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

    private void ImportFromClipboard()
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (Exception)
        {
            StatusText = L["status.clipReadFail"];
            return;
        }

        AddLinks(text, L["status.noLinksClip"]);
    }

    private void ImportFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L["import.file"],
            Filter = "*.conf;*.txt|*.conf;*.txt;*.conf.txt|*.*|*.*",
        };
        if (dlg.ShowDialog() != true)
            return;

        string text;
        try
        {
            text = File.ReadAllText(dlg.FileName);
        }
        catch (Exception ex)
        {
            StatusText = L.Format("status.fileReadFail", ex.Message);
            return;
        }

        AddLinks(text, L["status.noLinksFile"]);
    }

    private void ImportQrFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L["import.qrFile"],
            Filter = "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff;*.webp|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff;*.webp|*.*|*.*",
        };
        if (dlg.ShowDialog() != true)
            return;

        string? text;
        try
        {
            text = QrDecoder.DecodeFile(dlg.FileName);
        }
        catch (Exception ex)
        {
            StatusText = L.Format("status.imgReadFail", ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = L["status.qrNotRecognized"];
            return;
        }
        AddLinks(text, L["status.noLinksQr"]);
    }

    private void ImportQrFromClipboard()
    {
        System.Windows.Media.Imaging.BitmapSource? image;
        try
        {
            image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
        }
        catch (Exception)
        {
            StatusText = L["status.imgClipReadFail"];
            return;
        }

        if (image is null)
        {
            StatusText = L["status.noImgClip"];
            return;
        }

        string? text;
        try
        {
            text = QrDecoder.Decode(image);
        }
        catch (Exception ex)
        {
            StatusText = L.Format("status.qrError", ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = L["status.qrNotRecognized"];
            return;
        }
        AddLinks(text, L["status.noLinksQr"]);
    }

    private void ImportQrFromWebcam()
    {
        var dlg = new WebcamQrWindow { Owner = Application.Current?.MainWindow };
        var ok = dlg.ShowDialog();
        if (ok == true && !string.IsNullOrWhiteSpace(dlg.Result))
            AddLinks(dlg.Result, L["status.noLinksQr"]);
    }

    /// <summary>Parses share-links from <paramref name="text"/>, adds new ones, persists.</summary>
    private void AddLinks(string text, string noneFoundMessage)
    {
        var parsed = ShareLinkParser.ParseMany(text);
        if (parsed.Count == 0)
        {
            StatusText = noneFoundMessage;
            return;
        }

        int added = 0;
        foreach (var p in parsed)
        {
            if (Profiles.Any(x => x.Raw == p.Raw))
                continue;
            Profiles.Add(p);
            added++;
        }

        SelectedProfile ??= Profiles.FirstOrDefault();
        SaveProfiles();
        StatusText = added > 0 ? L.Format("status.profilesAdded", added) : L["status.profilesExist"];
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
            p.LatencyMs = await LatencyProbe.MeasureAsync(p.Server, p.Port)));

        // Reachable/fastest first; unreachable (null) last, then by name.
        ReplaceProfiles(list
            .OrderBy(p => p.LatencyMs ?? int.MaxValue)
            .ThenBy(p => p.Tag, StringComparer.OrdinalIgnoreCase));
        SaveProfiles();
        StatusText = L["status.pingDone"];
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
        });

    private void OnEngineLog(object? sender, string line) =>
        _dispatcher.BeginInvoke(() =>
        {
            var combined = Log + line + Environment.NewLine;
            if (combined.Length > MaxLogChars)
                combined = combined[^MaxLogChars..];
            Log = combined;
        });

    public async Task ShutdownAsync() => await _engine.DisposeAsync();
}
