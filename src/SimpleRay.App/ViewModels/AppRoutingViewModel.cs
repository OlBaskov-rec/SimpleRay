using System.Collections.ObjectModel;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Services;
using SimpleRay.Core.Models;

namespace SimpleRay.App.ViewModels;

/// <summary>
/// Edits the per-app routing lists (proxy / direct) of a <see cref="RoutingSettings"/>.
/// Operates on the live settings instance; the host persists on close.
/// </summary>
public sealed class AppRoutingViewModel : ObservableObject
{
    private readonly RoutingSettings _routing;

    private string? _selectedRunning;
    private string? _selectedProxy;
    private string? _selectedDirect;
    private string _manualName = "";

    public AppRoutingViewModel(RoutingSettings routing)
    {
        _routing = routing;

        foreach (var n in routing.ProxyProcesses) ProxyApps.Add(n);
        foreach (var n in routing.DirectProcesses) DirectApps.Add(n);

        AddProxyCommand = new RelayCommand(() => Add(ProxyApps, PickName()), () => CanAdd);
        AddDirectCommand = new RelayCommand(() => Add(DirectApps, PickName()), () => CanAdd);
        RemoveProxyCommand = new RelayCommand(() => Remove(ProxyApps, ref _selectedProxy, nameof(SelectedProxy)),
            () => SelectedProxy is not null);
        RemoveDirectCommand = new RelayCommand(() => Remove(DirectApps, ref _selectedDirect, nameof(SelectedDirect)),
            () => SelectedDirect is not null);
        RefreshCommand = new RelayCommand(RefreshRunning);

        RefreshRunning();
    }

    public ObservableCollection<string> RunningProcesses { get; } = new();
    public ObservableCollection<string> ProxyApps { get; } = new();
    public ObservableCollection<string> DirectApps { get; } = new();

    public string? SelectedRunning
    {
        get => _selectedRunning;
        set { if (SetField(ref _selectedRunning, value)) RaiseAddCanExecute(); }
    }

    public string? SelectedProxy
    {
        get => _selectedProxy;
        set { if (SetField(ref _selectedProxy, value)) RemoveProxyCommand.RaiseCanExecuteChanged(); }
    }

    public string? SelectedDirect
    {
        get => _selectedDirect;
        set { if (SetField(ref _selectedDirect, value)) RemoveDirectCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Free-typed process name (with or without ".exe").</summary>
    public string ManualName
    {
        get => _manualName;
        set { if (SetField(ref _manualName, value)) RaiseAddCanExecute(); }
    }

    public RelayCommand AddProxyCommand { get; }
    public RelayCommand AddDirectCommand { get; }
    public RelayCommand RemoveProxyCommand { get; }
    public RelayCommand RemoveDirectCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private bool CanAdd => !string.IsNullOrWhiteSpace(PickName());

    /// <summary>Manual text wins over the running-process selection.</summary>
    private string PickName()
    {
        var raw = !string.IsNullOrWhiteSpace(ManualName) ? ManualName : SelectedRunning ?? "";
        raw = raw.Trim();
        if (raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            raw = raw[..^4];
        return raw;
    }

    private void Add(ObservableCollection<string> target, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        // A process belongs to exactly one bucket — drop it from the other.
        var other = ReferenceEquals(target, ProxyApps) ? DirectApps : ProxyApps;
        RemoveCaseInsensitive(other, name);

        if (!target.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            target.Add(name);

        ManualName = "";
        Commit();
    }

    private void Remove(ObservableCollection<string> list, ref string? selected, string selectedPropName)
    {
        if (selected is null) return;
        list.Remove(selected);
        selected = null;
        OnPropertyChanged(selectedPropName);
        Commit();
    }

    private static void RemoveCaseInsensitive(ObservableCollection<string> list, string name)
    {
        var existing = list.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) list.Remove(existing);
    }

    private void RefreshRunning()
    {
        RunningProcesses.Clear();
        foreach (var n in ProcessList.RunningNames())
            RunningProcesses.Add(n);
    }

    /// <summary>Push the edited lists back into the shared settings instance.</summary>
    private void Commit()
    {
        _routing.ProxyProcesses = ProxyApps.ToList();
        _routing.DirectProcesses = DirectApps.ToList();
    }

    private void RaiseAddCanExecute()
    {
        AddProxyCommand.RaiseCanExecuteChanged();
        AddDirectCommand.RaiseCanExecuteChanged();
    }
}
