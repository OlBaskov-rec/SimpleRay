namespace SimpleRay.Core.Dns;

/// <summary>
/// The public resolvers offered in the UI. DoH entries were verified to answer RFC 8484
/// queries on their bare IP; providers that only serve DoH under a hostname (they would
/// need a bootstrap resolver) are intentionally absent. UDP entries answer plain DNS and
/// are offered only for direct traffic — never for the tunnelled resolver, which must
/// stay encrypted.
/// </summary>
public static class DnsCatalog
{
    /// <summary>Fallback when a stored id is unknown — reachable from nearly everywhere.</summary>
    public const string FallbackId = "cloudflare";

    /// <summary>Every resolver, in the order shown to the user for the direct (local) picker.</summary>
    public static IReadOnlyList<DnsProvider> All { get; } = new[]
    {
        new DnsProvider("cloudflare", "Cloudflare (1.1.1.1)", "1.1.1.1"),
        new DnsProvider("google", "Google (8.8.8.8)", "8.8.8.8"),
        new DnsProvider("adguard", "AdGuard (94.140.14.14)", "94.140.14.14"),
        new DnsProvider("quad9", "Quad9 (9.9.9.9)", "9.9.9.9"),
        new DnsProvider("alidns", "AliDNS (223.5.5.5)", "223.5.5.5"),
        // Yandex serves DoH only under a hostname (common.dns.yandex.net) — its bare IP
        // rejects TLS — so it is offered as plain UDP, which is acceptable for direct
        // traffic (this path is not the censorship-sensitive one) but never tunnelled.
        new DnsProvider("yandex", "Yandex (77.88.8.8, UDP)", "77.88.8.8", DnsTransport.Udp),
    };

    /// <summary>Resolvers eligible for the tunnelled lookup: encrypted transports only.</summary>
    public static IReadOnlyList<DnsProvider> RemoteChoices { get; } =
        All.Where(p => p.Transport == DnsTransport.Doh).ToList();

    /// <summary>The provider with this id, or null when unknown.</summary>
    public static DnsProvider? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id == id);

    /// <summary>The provider for direct traffic; unknown ids fall back to <see cref="FallbackId"/>.</summary>
    public static DnsProvider ResolveLocal(string? id) =>
        Find(id) ?? Find(FallbackId)!;

    /// <summary>
    /// The provider for tunnelled traffic. A UDP (or unknown) id falls back to the default
    /// DoH resolver, so a plaintext query can never be sent through the tunnel.
    /// </summary>
    public static DnsProvider ResolveRemote(string? id)
    {
        var p = Find(id);
        return p is { Transport: DnsTransport.Doh } ? p : Find(FallbackId)!;
    }

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
