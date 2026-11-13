namespace SimpleRay.Core.Dns;

/// <summary>Connection-specific DNS discovered at runtime; never persisted in settings.</summary>
public sealed record LanDns(IReadOnlyList<string> Suffixes, string Server);
