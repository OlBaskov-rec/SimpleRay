using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SimpleRay.App.Engine;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Services;
using SimpleRay.Core.Config;
using SimpleRay.Core.Engine;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;

namespace SimpleRay.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogChars = 20_000;

    private readonly ProfileStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly UpdateService _updateService = new();
    private readonly IVpnEngine _engine;
    private readonly RoutingSettings _routing;
    private readonly Dispatcher _dispatcher;

    // geosite/geoip tags backing the routing presets (must exist as *.srs in GeoDir).
    private const string TagRuSites = "geosite-category-ru";
    private const string TagRuIp = "geoip-ru";
    private const string TagPrivate = "geosite-private";

    private ProfileConfig? _selectedProfile;
    private bool _isConnected;
    private string _statusText = "Отключено";
    private string _log = "";
    private string _activeLabel = "";

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _routing = _settingsStore.Load();

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
        OpenAppsCommand = new RelayCommand(OpenAppRouting);
        GroupChangedCommand = new RelayCommand(OnGroupChanged);
        CheckUpdatesCommand = new RelayCommand(CheckForUpdatesAsync);
    }

    public string AppVersion => "v" + UpdateService.CurrentVersion.ToString(3);

    public ObservableCollection<ProfileConfig> Profiles { get; } = new();

    public ProfileConfig? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value))
                RemoveCommand.RaiseCanExecuteChanged();
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

    public string ConnectButtonText => IsConnected ? "Отключить" : "Подключить";

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
    public RelayCommand OpenAppsCommand { get; }
    public RelayCommand GroupChangedCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }

    private async Task CheckForUpdatesAsync()
    {
        if (IsConnected)
        {
            StatusText = "Сначала отключитесь, потом обновляйтесь";
            return;
        }

        StatusText = "Проверка обновлений…";
        SimpleRay.Core.Update.ReleaseInfo? info;
        try
        {
            info = await _updateService.CheckAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Не удалось проверить обновления: " + ex.Message;
            return;
        }

        if (info is null)
        {
            StatusText = $"У вас последняя версия ({AppVersion})";
            return;
        }

        var consent = MessageBox.Show(
            $"Доступна версия {info.Version.ToString(3)} (у вас {UpdateService.CurrentVersion.ToString(3)}).\n\n" +
            "Скачать и обновить сейчас? Приложение перезапустится.",
            "Обновление SimpleRay", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (consent != MessageBoxResult.Yes)
        {
            StatusText = "Обновление отложено";
            return;
        }

        try
        {
            var progress = new Progress<double>(p => StatusText = $"Загрузка обновления… {p:P0}");
            var staging = await _updateService.DownloadAndStageAsync(info, progress);

            StatusText = "Применение обновления…";
            await _engine.DisposeAsync(); // stop sing-box so its files aren't locked
            _updateService.ApplyAndRestart(staging);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка обновления: " + ex.Message;
        }
    }

    // --- Failover group ---------------------------------------------------

    public sealed record FailoverModeItem(string Label, FailoverMode Mode);

    public IReadOnlyList<FailoverModeItem> FailoverModes { get; } = new[]
    {
        new FailoverModeItem("Авто по скорости (urltest)", FailoverMode.UrlTest),
        new FailoverModeItem("Ручной выбор (selector)", FailoverMode.Selector),
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
            return n >= 2
                ? $"В группе серверов: {n} — подключение пойдёт через группу с автопереключением"
                : "Отметьте 2+ сервера, чтобы включить автопереключение между каналами";
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

    public IReadOnlyList<RoutingModeItem> Modes { get; } = new[]
    {
        new RoutingModeItem("Глобально — весь трафик через VPN", RoutingMode.Global),
        new RoutingModeItem("По правилам — разделять (geo)", RoutingMode.Rule),
        new RoutingModeItem("Напрямую — без VPN", RoutingMode.Direct),
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
        catch (Exception ex) { StatusText = "Не удалось сохранить настройки: " + ex.Message; }
    }

    private void SaveProfiles()
    {
        try { _store.Save(Profiles); }
        catch (Exception ex) { StatusText = "Не удалось сохранить профили: " + ex.Message; }
    }

    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            try { await _engine.StopAsync(); }
            catch (Exception ex) { StatusText = "Ошибка отключения: " + ex.Message; }
            return;
        }

        var targets = ResolveConnectTargets();
        if (targets.Count == 0)
        {
            StatusText = "Выберите профиль или отметьте серверы в группу";
            return;
        }

        if (!File.Exists(AppPaths.CoreExe))
        {
            StatusText = $"sing-box.exe не найден: {AppPaths.CoreExe}";
            return;
        }

        if (!Elevation.IsElevated())
        {
            StatusText = "Нужны права администратора (TUN). Перезапуск…";
            if (!Elevation.RelaunchElevated())
                StatusText = "Запуск с правами администратора отменён";
            else
                Application.Current.Shutdown();
            return;
        }

        try
        {
            _activeLabel = targets.Count > 1
                ? $"группа ({targets.Count}, {(SelectedFailoverMode == FailoverMode.UrlTest ? "авто" : "ручной")})"
                : targets[0].Tag;
            StatusText = "Подключение…";
            var options = new GeneratorOptions { RuleSetDirectory = AppPaths.GeoDir };
            var configJson = SingBoxConfigGenerator.GenerateJson(targets, _routing, options);
            await _engine.StartAsync(configJson);
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
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
            StatusText = "Не удалось прочитать буфер обмена";
            return;
        }

        AddLinks(text, "В буфере нет распознанных ссылок");
    }

    private void ImportFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите файл профиля (.conf / .txt со ссылками)",
            Filter = "Профили|*.conf;*.txt;*.conf.txt|Все файлы|*.*",
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
            StatusText = "Не удалось прочитать файл: " + ex.Message;
            return;
        }

        AddLinks(text, "В файле нет распознанных профилей");
    }

    private void ImportQrFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите изображение с QR-кодом",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff;*.webp|Все файлы|*.*",
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
            StatusText = "Не удалось прочитать изображение: " + ex.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "QR-код не распознан";
            return;
        }
        AddLinks(text, "В QR-коде нет распознанных ссылок");
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
            StatusText = "Не удалось прочитать изображение из буфера";
            return;
        }

        if (image is null)
        {
            StatusText = "В буфере нет изображения";
            return;
        }

        string? text;
        try
        {
            text = QrDecoder.Decode(image);
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка распознавания: " + ex.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "QR-код не распознан";
            return;
        }
        AddLinks(text, "В QR-коде нет распознанных ссылок");
    }

    private void ImportQrFromWebcam()
    {
        var dlg = new WebcamQrWindow { Owner = Application.Current?.MainWindow };
        var ok = dlg.ShowDialog();
        if (ok == true && !string.IsNullOrWhiteSpace(dlg.Result))
            AddLinks(dlg.Result, "В QR-коде нет распознанных ссылок");
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
        StatusText = added > 0 ? $"Добавлено профилей: {added}" : "Профили уже есть в списке";
    }

    private void RemoveSelected()
    {
        if (SelectedProfile is null)
            return;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        SaveProfiles();
    }

    // Engine events arrive on thread-pool threads. BeginInvoke (not Invoke): a blocking
    // dispatch would deadlock the synchronous engine shutdown in MainWindow.Closed.
    private void OnEngineStateChanged(object? sender, EngineState state) =>
        _dispatcher.BeginInvoke(() =>
        {
            IsConnected = state == EngineState.Running;
            StatusText = state switch
            {
                EngineState.Running => $"Подключено: {_activeLabel}",
                EngineState.Starting => "Подключение…",
                EngineState.Stopping => "Отключение…",
                EngineState.Faulted => "Сбой движка — см. лог",
                _ => "Отключено",
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
