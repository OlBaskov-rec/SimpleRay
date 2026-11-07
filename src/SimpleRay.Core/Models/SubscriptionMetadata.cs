namespace SimpleRay.Core.Models;

/// <summary>
/// Provider-supplied information about a subscription, sent as HTTP response headers or as
/// <c>#key: value</c> lines at the top of the body (the Happ / v2rayN convention):
/// <c>profile-title</c>, <c>subscription-userinfo</c>, <c>profile-update-interval</c>,
/// <c>support-url</c>, <c>profile-web-page-url</c>, <c>announce</c>. Every field is optional.
/// </summary>
public sealed class SubscriptionMetadata
{
    /// <summary>Update interval used when the provider doesn't send <c>profile-update-interval</c>.</summary>
    public static readonly TimeSpan DefaultUpdateInterval = TimeSpan.FromHours(24);

    public string? Title { get; set; }

    /// <summary>Bytes uploaded / downloaded so far (subscription-userinfo).</summary>
    public long? Upload { get; set; }
    public long? Download { get; set; }

    /// <summary>Traffic quota in bytes; null or 0 = unlimited.</summary>
    public long? Total { get; set; }

    /// <summary>Expiry moment; null = never expires (the header sends 0 for that).</summary>
    public DateTimeOffset? Expire { get; set; }

    /// <summary>Provider-requested auto-update interval in hours (profile-update-interval).</summary>
    public int? UpdateIntervalHours { get; set; }

    public string? SupportUrl { get; set; }
    public string? WebPageUrl { get; set; }
    public string? Announce { get; set; }

    /// <summary>Upload + download, or null when the provider reported neither.</summary>
    public long? Used => Upload is null && Download is null ? null : (Upload ?? 0) + (Download ?? 0);

    public TimeSpan UpdateInterval =>
        UpdateIntervalHours is > 0 ? TimeSpan.FromHours(UpdateIntervalHours.Value) : DefaultUpdateInterval;

    /// <summary>True when an auto-update is due: never fetched, or the interval has elapsed.</summary>
    public bool IsUpdateDue(DateTimeOffset? lastUpdated, DateTimeOffset now) =>
        lastUpdated is null || now - lastUpdated.Value >= UpdateInterval;
}
