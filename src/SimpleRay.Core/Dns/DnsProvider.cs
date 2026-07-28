namespace SimpleRay.Core.Dns;

/// <summary>
/// A public DNS-over-HTTPS resolver the user can pick from.
///
/// <see cref="Server"/> is deliberately an IP literal, never a hostname: the resolver
/// is what resolves everything else, so a hostname here would need its own bootstrap
/// resolver first. Only providers that serve DoH directly on their IP (with a
/// certificate valid for it) belong in the catalog — verified with <see cref="Net.DohProbe"/>.
/// </summary>
public sealed record DnsProvider(string Id, string Label, string Server);
