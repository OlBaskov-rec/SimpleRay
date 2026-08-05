namespace SimpleRay.Core.Models;

public enum RoutingMode
{
    /// <summary>Everything except private/LAN addresses goes through the proxy.</summary>
    Global,

    /// <summary>Split by geoip/geosite rules; only matched traffic goes direct.</summary>
    Rule,

    /// <summary>Nothing is proxied (kill-switch off / testing).</summary>
    Direct
}

/// <summary>
/// User-facing routing configuration. Translated into sing-box route rules by
/// <see cref="Config.SingBoxConfigGenerator"/>.
/// </summary>
public sealed class RoutingSettings
{
    public RoutingMode Mode { get; set; } = RoutingMode.Rule;

    /// <summary>geosite categories routed directly (e.g. "geosite-ru", "geosite-private").</summary>
    public List<string> DirectGeosite { get; set; } = new() { "geosite-private" };

    /// <summary>geoip country codes routed directly (e.g. "geoip-ru").</summary>
    public List<string> DirectGeoip { get; set; } = new();

    /// <summary>Block ads/trackers via geosite category-ads-all.</summary>
    public bool BlockAds { get; set; } = true;

    /// <summary>Process names (without .exe) forced through the proxy.</summary>
    public List<string> ProxyProcesses { get; set; } = new();

    /// <summary>Process names (without .exe) forced to bypass the proxy.</summary>
    public List<string> DirectProcesses { get; set; } = new();

    /// <summary>Failover group settings, used when more than one server is connected together.</summary>
    public GroupSettings Failover { get; set; } = new();

    /// <summary>Connect automatically on startup (to the failover group or the first profile).</summary>
    public bool AutoConnect { get; set; }

    /// <summary>Which public DoH resolvers to use for direct and tunnelled lookups.</summary>
    public DnsSettings Dns { get; set; } = new();

    /// <summary>Block all non-tunnel traffic while connected (kill-switch). Off by default.</summary>
    public bool KillSwitch { get; set; }
}
