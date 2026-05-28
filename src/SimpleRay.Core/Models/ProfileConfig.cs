namespace SimpleRay.Core.Models;

public enum ProxyProtocol
{
    VLESS,
    VMess,
    Trojan,
    Shadowsocks
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

    /// <summary>Original share link, kept for round-tripping / debugging.</summary>
    public string? Raw { get; set; }
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
