using SimpleRay.Core.Dns;

namespace SimpleRay.Core.Models;

/// <summary>
/// Which public resolvers to use. Two are needed because they answer different
/// questions: <see cref="LocalProviderId"/> resolves names for traffic that bypasses
/// the tunnel (and the proxy servers' own hostnames), so it must be reachable and fast
/// from where the user physically is; <see cref="RemoteProviderId"/> is queried through
/// the tunnel, so the user's location doesn't affect it.
/// </summary>
public sealed class DnsSettings
{
    /// <summary>Catalog id of the resolver used for direct traffic. Unknown ids fall back.</summary>
    public string LocalProviderId { get; set; } = DnsCatalog.FallbackId;

    /// <summary>Catalog id of the resolver queried through the tunnel.</summary>
    public string RemoteProviderId { get; set; } = DnsCatalog.FallbackId;
}
