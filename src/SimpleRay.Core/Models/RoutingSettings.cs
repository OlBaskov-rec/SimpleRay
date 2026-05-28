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
}
