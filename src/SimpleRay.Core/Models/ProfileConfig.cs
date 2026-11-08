using System.ComponentModel;
using System.Text.Json.Serialization;

namespace SimpleRay.Core.Models;

public enum ProxyProtocol
{
    VLESS,
    VMess,
    Trojan,
    Shadowsocks,
    WireGuard,
    // Appended so existing numeric values stay stable.
    Hysteria2,
    Socks
}

/// <summary>
/// Protocol-agnostic representation of a single proxy server, decoded from a
/// share link. The engine layer turns this into a sing-box outbound.
/// </summary>
public sealed class ProfileConfig : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Tag { get; set; } = "";
    public ProxyProtocol Protocol { get; set; }
    public string Server { get; set; } = "";
    public int Port { get; set; }

    // Credentials (which one is used depends on Protocol).
    public string? Uuid { get; set; }       // VLESS / VMess
    public string? Password { get; set; }   // Trojan / Shadowsocks / Hysteria2 auth / SOCKS
    public string? Username { get; set; }   // SOCKS
    public string? Method { get; set; }     // Shadowsocks cipher / VMess security
    public int AlterId { get; set; }        // VMess legacy
    public string? Flow { get; set; }       // VLESS flow (e.g. xtls-rprx-vision)

    // Transport.
    public string Network { get; set; } = "tcp";   // tcp | ws | grpc | http | quic
    public string? Path { get; set; }              // ws / http path
    public string? Host { get; set; }              // ws / http Host header
    public string? ServiceName { get; set; }       // grpc service name

    public TlsSettings? Tls { get; set; }

    // Hysteria2.
    public string? ObfsType { get; set; }          // "salamander"
    public string? ObfsPassword { get; set; }
    public int? UpMbps { get; set; }
    public int? DownMbps { get; set; }

    /// <summary>Port hopping: ports/ranges the client hops across ("443", "20000-30000"); null = just <see cref="Port"/>.</summary>
    public List<string>? PortRanges { get; set; }

    /// <summary>How often to hop ports, e.g. "30s"; null = sing-box default.</summary>
    public string? HopInterval { get; set; }

    /// <summary>True for protocols that run over UDP (a TCP ping can't measure them).</summary>
    [JsonIgnore]
    public bool IsUdpBased => Protocol is ProxyProtocol.WireGuard or ProxyProtocol.Hysteria2;

    /// <summary>WireGuard-specific settings (only when <see cref="Protocol"/> is WireGuard).</summary>
    public WireGuardSettings? Wg { get; set; }

    /// <summary>Original share link, kept for round-tripping / debugging.</summary>
    public string? Raw { get; set; }

    /// <summary>Whether this server is a member of the failover group (UI state, persisted).</summary>
    public bool InGroup { get; set; }

    /// <summary>Source subscription URL if this profile came from one; null when added manually.</summary>
    public string? Subscription { get; set; }

    /// <summary>True when TLS is on but certificate validation is disabled (allowInsecure) — a security risk.</summary>
    [JsonIgnore]
    public bool SkipsCertCheck => Tls is { Enabled: true, AllowInsecure: true };

    /// <summary>Last measured direct TCP latency in ms (transient, not persisted); null = untested/unreachable.</summary>
    [JsonIgnore]
    public int? LatencyMs { get; set; }

    private string? _latencyText;

    /// <summary>
    /// Display text for the latest ping (transient): "&lt;n&gt; ms" when reachable, a dash when
    /// unreachable, null before the first ping. Lets the UI distinguish "never pinged" (hide)
    /// from "pinged but unreachable" (show a gray dash) — <see cref="LatencyMs"/> can't, since
    /// both are null.
    /// </summary>
    [JsonIgnore]
    public string? LatencyText
    {
        get => _latencyText;
        set { if (_latencyText != value) { _latencyText = value; OnPropertyChanged(nameof(LatencyText)); } }
    }

    private bool _isActive;

    /// <summary>True while this server is the currently connected one (transient, drives list highlight).</summary>
    [JsonIgnore]
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
