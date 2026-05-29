using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
    private readonly EngineManager _engine;
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

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _routing = _settingsStore.Load();

        foreach (var p in _store.Load())
            Profiles.Add(p);
        _selectedProfile = Profiles.FirstOrDefault();

        _engine = new EngineManager(new EngineOptions
        {
            ExecutablePath = AppPaths.CoreExe,
            WorkingDirectory = AppPaths.RuntimeDir,
        });
        _engine.StateChanged += OnEngineStateChanged;
        _engine.LogReceived += OnEngineLog;

        ConnectCommand = new RelayCommand(ToggleConnectionAsync);
        ImportClipboardCommand = new RelayCommand(ImportFromClipboard);
        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedProfile is not null);
        OpenAppsCommand = new RelayCommand(OpenAppRouting);
    }

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
    public RelayCommand RemoveCommand { get; }
    public RelayCommand OpenAppsCommand { get; }

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

    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            await _engine.StopAsync();
            return;
        }

        if (SelectedProfile is null)
        {
            StatusText = "Выберите профиль";
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
            StatusText = "Подключение…";
            var options = new GeneratorOptions { RuleSetDirectory = AppPaths.GeoDir };
            var configJson = SingBoxConfigGenerator.GenerateJson(SelectedProfile, _routing, options);
            await _engine.StartAsync(configJson);
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
        }
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

        var parsed = ShareLinkParser.ParseMany(text);
        if (parsed.Count == 0)
        {
            StatusText = "В буфере нет распознанных ссылок";
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
        _store.Save(Profiles);
        StatusText = added > 0 ? $"Добавлено профилей: {added}" : "Профили уже есть в списке";
    }

    private void RemoveSelected()
    {
        if (SelectedProfile is null)
            return;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        _store.Save(Profiles);
    }

    private void OnEngineStateChanged(object? sender, EngineState state) =>
        _dispatcher.Invoke(() =>
        {
            IsConnected = state == EngineState.Running;
            StatusText = state switch
            {
                EngineState.Running => $"Подключено: {SelectedProfile?.Tag}",
                EngineState.Starting => "Подключение…",
                EngineState.Stopping => "Отключение…",
                EngineState.Faulted => "Сбой движка — см. лог",
                _ => "Отключено",
            };
        });

    private void OnEngineLog(object? sender, string line) =>
        _dispatcher.Invoke(() =>
        {
            var combined = Log + line + Environment.NewLine;
            if (combined.Length > MaxLogChars)
                combined = combined[^MaxLogChars..];
            Log = combined;
        });

    public async Task ShutdownAsync() => await _engine.DisposeAsync();
}
