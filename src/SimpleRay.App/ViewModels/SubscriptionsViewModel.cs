using System.Collections.ObjectModel;
using System.ComponentModel;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Localization;
using SimpleRay.App.Services;
using SimpleRay.Core.Models;

namespace SimpleRay.App.ViewModels;

/// <summary>
/// What the subscriptions window needs from the main view-model, which owns the subscription
/// list and the profiles they feed (fetching, merging and dialogs stay there).
/// </summary>
public interface ISubscriptionHost : INotifyPropertyChanged
{
    IReadOnlyList<SubscriptionEntry> Subscriptions { get; }
    string StatusText { get; }
    int ProfileCount(string url);
    Task AddSubscriptionAsync();
    Task RefreshSubscriptionsAsync(IReadOnlyList<SubscriptionEntry> entries);
    Task EditSubscriptionAsync(SubscriptionEntry entry);
    void RemoveSubscription(SubscriptionEntry entry);

    /// <summary>Raised after any subscription is added, refreshed, edited or removed.</summary>
    event EventHandler? SubscriptionsChanged;
}

/// <summary>One row of the subscriptions window: a snapshot rebuilt whenever the list changes.</summary>
public sealed record SubscriptionItem(SubscriptionEntry Entry, string DisplayName, string Details);

/// <summary>View-model of the subscriptions window: list, selection and the per-row actions.</summary>
public sealed class SubscriptionsViewModel : ObservableObject
{
    private static LocalizationManager L => LocalizationManager.Instance;

    private SubscriptionItem? _selected;

    public SubscriptionsViewModel(ISubscriptionHost host)
    {
        Host = host;
        AddCommand = new RelayCommand(host.AddSubscriptionAsync);
        RefreshCommand = new RelayCommand(
            () => host.RefreshSubscriptionsAsync(new[] { Selected!.Entry }), () => Selected is not null);
        RefreshAllCommand = new RelayCommand(
            () => host.RefreshSubscriptionsAsync(host.Subscriptions.ToList()), () => Items.Count > 0);
        EditCommand = new RelayCommand(() => host.EditSubscriptionAsync(Selected!.Entry), () => Selected is not null);
        RemoveCommand = new RelayCommand(() => host.RemoveSubscription(Selected!.Entry), () => Selected is not null);

        host.SubscriptionsChanged += OnHostChanged;
        Reload();
    }

    /// <summary>The main view-model; the window binds its status line to Host.StatusText.</summary>
    public ISubscriptionHost Host { get; }

    public ObservableCollection<SubscriptionItem> Items { get; } = new();

    public bool IsEmpty => Items.Count == 0;

    public SubscriptionItem? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                EditCommand.RaiseCanExecuteChanged();
                RemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand AddCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand RefreshAllCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand RemoveCommand { get; }

    /// <summary>Unsubscribes from the host; call when the window closes.</summary>
    public void Detach() => Host.SubscriptionsChanged -= OnHostChanged;

    private void OnHostChanged(object? sender, EventArgs e) => Reload();

    /// <summary>Rebuilds the rows, keeping the selection on the same URL.</summary>
    private void Reload()
    {
        var selectedUrl = Selected?.Entry.Url;
        Items.Clear();
        foreach (var entry in Host.Subscriptions)
            Items.Add(new SubscriptionItem(entry, entry.DisplayName, Details(entry)));
        Selected = Items.FirstOrDefault(i => i.Entry.Url == selectedUrl) ?? Items.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        RefreshAllCommand.RaiseCanExecuteChanged();
    }

    private string Details(SubscriptionEntry e)
    {
        var host = Uri.TryCreate(e.Url, UriKind.Absolute, out var u) ? u.Host : e.Url;
        var updated = e.LastUpdated is { } t
            ? L.Format("subs.updated", t.ToLocalTime().ToString("g"))
            : L["subs.neverUpdated"];
        var lines = new List<string>
        {
            $"{host} · {L.Format("subs.servers", Host.ProfileCount(e.Url))} · {updated}",
        };
        lines.AddRange(SubscriptionText.InfoLines(e.Info, includeTitle: false, includeAnnounce: false));
        if (e.UserAgent is not null)
            lines.Add("User-Agent: " + e.UserAgent);
        return string.Join("\n", lines);
    }
}

/// <summary>Localized text for a subscription's provider metadata, shared by the main window and the list.</summary>
public static class SubscriptionText
{
    private static LocalizationManager L => LocalizationManager.Instance;

    public static IEnumerable<string> InfoLines(SubscriptionMetadata info, bool includeTitle, bool includeAnnounce)
    {
        if (includeTitle && info.Title is not null)
            yield return info.Title;
        if (info.Used is long used)
            yield return info.Total is long total
                ? L.Format("sub.info.traffic", FormatBytes(used), FormatBytes(total))
                : L.Format("sub.info.trafficUnlimited", FormatBytes(used));
        if (info.Expire is DateTimeOffset expire)
            yield return expire <= DateTimeOffset.Now
                ? L["sub.info.expired"]
                : L.Format("sub.info.expires", expire.ToLocalTime().ToString("d")); // UI culture: SetCulture sets CurrentCulture
        if (includeAnnounce && info.Announce is not null)
            yield return info.Announce;
    }

    public static string FormatBytes(long bytes)
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
}
