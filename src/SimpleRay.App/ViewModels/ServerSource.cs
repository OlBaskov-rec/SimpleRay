using SimpleRay.App.Localization;
using SimpleRay.App.Services;
using SimpleRay.Core.Models;

namespace SimpleRay.App.ViewModels;

public enum ServerSourceKind { All, Manual, Subscription }

/// <summary>
/// One row of the subscriptions panel: "All servers", "Added manually" or a subscription.
/// Selecting it filters the server list to <see cref="Matches"/>.
/// </summary>
public sealed record ServerSource(ServerSourceKind Kind, string Title, string Details, SubscriptionEntry? Entry = null)
{
    /// <summary>Stable identity across rebuilds (to keep the selection).</summary>
    public string Key => Kind == ServerSourceKind.Subscription ? "sub:" + Entry!.Url : Kind.ToString();

    public bool Matches(ProfileConfig p) => Kind switch
    {
        ServerSourceKind.All => true,
        ServerSourceKind.Manual => p.Subscription is null,
        _ => p.Subscription == Entry!.Url,
    };

    private static LocalizationManager L => LocalizationManager.Instance;

    /// <summary>All servers, then manual ones (only when there are any), then each subscription.</summary>
    public static List<ServerSource> Build(IReadOnlyList<SubscriptionEntry> subscriptions, IReadOnlyCollection<ProfileConfig> profiles)
    {
        var list = new List<ServerSource>
        {
            new(ServerSourceKind.All, L["sources.all"], L.Format("subs.servers", profiles.Count)),
        };
        int manual = profiles.Count(p => p.Subscription is null);
        if (manual > 0 || subscriptions.Count == 0)
            list.Add(new(ServerSourceKind.Manual, L["sources.manual"], L.Format("subs.servers", manual)));
        foreach (var e in subscriptions)
            list.Add(new(ServerSourceKind.Subscription, e.DisplayName, DescribeSubscription(e, profiles), e));
        return list;
    }

    private static string DescribeSubscription(SubscriptionEntry e, IReadOnlyCollection<ProfileConfig> profiles)
    {
        var lines = new List<string> { L.Format("subs.servers", profiles.Count(p => p.Subscription == e.Url)) };
        lines.AddRange(SubscriptionText.InfoLines(e.Info, includeTitle: false, includeAnnounce: false));
        lines.Add(e.LastUpdated is { } t
            ? L.Format("subs.updated", t.ToLocalTime().ToString("g"))
            : L["subs.neverUpdated"]);
        return string.Join("\n", lines);
    }
}

/// <summary>Localized text for a subscription's provider metadata.</summary>
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
        if (info.Notices is { Count: > 0 } notices)
            yield return L.Format("sub.info.notice", string.Join(" · ", notices));
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
