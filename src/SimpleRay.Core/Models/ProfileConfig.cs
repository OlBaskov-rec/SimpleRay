using System.Text.Json.Serialization;

namespace SimpleRay.Core.Models;

public enum ProxyProtocol
{
    VLESS,
    VMess,
    Trojan,
    Shadowsocks,
    WireGuard
}

/// <summary>
/// Protocol-agnostic representation of a single proxy server, decoded from a
/// share link. The engine layer turns this into a sing-box outbound.
/// </summary>
public sealed class ProfileConfig
{
    public string Tag { get; set; } = "";
    public ProxyProtocol Protocol { get; set; }
    public string Server { get; set; } = "";
    public int Port { get; set; }

    // Credentials (which one is used depends on Protocol).
    public string? Uuid { get; set; }       // VLESS / VMess
    public string? Password { get; set; }   // Trojan / Shadowsocks
    public string? Method { get; set; }     // Shadowsocks cipher / VMess security
    public int AlterId { get; set; }        // VMess legacy
    public string? Flow { get; set; }       // VLESS flow (e.g. xtls-rprx-vision)

    // Transport.
    public string Network { get; set; } = "tcp";   // tcp | ws | grpc | http | quic
    public string? Path { get; set; }              // ws / http path
    public string? Host { get; set; }              // ws / http Host header
    public string? ServiceName { get; set; }       // grpc service name

    public TlsSettings? Tls { get; set; }

    /// <summary>WireGuard-specific settings (only when <see cref="Protocol"/> is WireGuard).</summary>
    public WireGuardSettings? Wg { get; set; }

    /// <summary>Original share link, kept for round-tripping / debugging.</summary>
    public string? Raw { get; set; }

    /// <summary>Whether this server is a member of the failover group (UI state, persisted).</summary>
    public bool InGroup { get; set; }

    /// <summary>True when TLS is on but certificate validation is disabled (allowInsecure) — a security risk.</summary>
    [JsonIgnore]
    public bool SkipsCertCheck => Tls is { Enabled: true, AllowInsecure: true };
}

/// <summary>
/// WireGuard tunnel parameters parsed from a standard [Interface]/[Peer] .conf.
/// The peer endpoint host/port live on <see cref="ProfileConfig.Server"/>/<see cref="ProfileConfig.Port"/>.
/// </summary>
public sealed class WireGuardSettings
{
    /// <summary>Interface PrivateKey.</summary>
    public string PrivateKey { get; set; } = "";

    /// <summary>Peer PublicKey.</summary>
    public string PeerPublicKey { get; set; } = "";

    /// <summary>Peer PresharedKey (optional).</summary>
    public string? PreSharedKey { get; set; }

    /// <summary>Interface Address list (local tunnel addresses, e.g. 10.0.0.2/32).</summary>
    public List<string> LocalAddresses { get; set; } = new();

    /// <summary>Peer AllowedIPs.</summary>
    public List<string> AllowedIps { get; set; } = new() { "0.0.0.0/0", "::/0" };

    /// <summary>Interface MTU (optional).</summary>
    public int? Mtu { get; set; }

    /// <summary>Peer PersistentKeepalive seconds (optional).</summary>
    public int? PersistentKeepalive { get; set; }
}

public sealed class TlsSettings
{
    public bool Enabled { get; set; }
    public string? ServerName { get; set; }
    public bool AllowInsecure { get; set; }
    public string[]? Alpn { get; set; }
    public string? Fingerprint { get; set; }   // uTLS fingerprint

    // REALITY.
    public bool IsReality { get; set; }
    public string? RealityPublicKey { get; set; }
    public string? RealityShortId { get; set; }
}
