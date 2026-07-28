namespace SimpleRay.Core.Dns;

/// <summary>How a resolver is reached.</summary>
public enum DnsTransport
{
    /// <summary>DNS-over-HTTPS. Encrypted; the only kind allowed for tunnelled lookups.</summary>
    Doh,

    /// <summary>Plain UDP DNS on port 53. Unencrypted — offered only for direct traffic.</summary>
    Udp,
}

/// <summary>
/// A public resolver the user can pick from.
///
/// <see cref="Server"/> is deliberately an IP literal, never a hostname: the resolver
/// is what resolves everything else, so a hostname here would need its own bootstrap
/// resolver first. DoH entries must serve DoH directly on their IP (certificate valid
/// for it) — verified with <see cref="Net.DohProbe"/>; UDP entries answer plain DNS on
/// :53 and are only offered for direct (non-tunnelled) traffic.
/// </summary>
public sealed record DnsProvider(string Id, string Label, string Server, DnsTransport Transport = DnsTransport.Doh);
