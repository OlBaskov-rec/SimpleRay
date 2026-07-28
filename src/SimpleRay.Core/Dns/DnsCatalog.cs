namespace SimpleRay.Core.Dns;

/// <summary>
/// The public DoH resolvers offered in the UI. Every entry was verified to answer
/// RFC 8484 queries on its bare IP; providers that only serve DoH under a hostname
/// (they would need a bootstrap resolver) are intentionally absent.
/// </summary>
public static class DnsCatalog
{
    /// <summary>Fallback when a stored id is unknown — reachable from nearly everywhere.</summary>
    public const string FallbackId = "cloudflare";

    public static IReadOnlyList<DnsProvider> All { get; } = new[]
    {
        new DnsProvider("cloudflare", "Cloudflare (1.1.1.1)", "1.1.1.1"),
        new DnsProvider("google", "Google (8.8.8.8)", "8.8.8.8"),
        new DnsProvider("adguard", "AdGuard (94.140.14.14)", "94.140.14.14"),
        new DnsProvider("quad9", "Quad9 (9.9.9.9)", "9.9.9.9"),
        new DnsProvider("alidns", "AliDNS (223.5.5.5)", "223.5.5.5"),
        // Yandex (77.88.8.8) is deliberately absent: it rejects TLS on its bare IP
        // (its DoH lives at common.dns.yandex.net), so it would need a bootstrap resolver.
    };

    /// <summary>The provider with this id, or null when unknown.</summary>
    public static DnsProvider? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id == id);

    /// <summary>The provider with this id, falling back to <see cref="FallbackId"/>.</summary>
    public static DnsProvider Resolve(string? id) =>
        Find(id) ?? Find(FallbackId)!;

    /// <summary>
    /// Starting point for a fresh install, by UI language. Only a coarse hint — the
    /// user is expected to run the availability check and pick whatever is actually
    /// fastest from where they are, which no static table can know.
    /// </summary>
    public static string InitialIdForLanguage(string? language) =>
        language is not null && language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "alidns"
            : FallbackId;
}
