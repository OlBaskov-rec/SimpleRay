namespace SimpleRay.Core.Models;

/// <summary>How a multi-server failover group selects its active outbound.</summary>
public enum FailoverMode
{
    /// <summary>sing-box urltest: auto-pick the lowest-latency outbound that passes the health check.</summary>
    UrlTest,

    /// <summary>sing-box selector: stay on a chosen outbound until changed.</summary>
    Selector
}

/// <summary>
/// Settings for grouping several servers into one failover outbound. Maps to a
/// sing-box <c>urltest</c> or <c>selector</c> outbound that becomes the single
/// "proxy" entry point used by routing.
/// </summary>
public sealed class GroupSettings
{
    public FailoverMode Mode { get; set; } = FailoverMode.UrlTest;

    /// <summary>Health-check URL for urltest mode.</summary>
    public string TestUrl { get; set; } = "https://www.gstatic.com/generate_204";

    /// <summary>How often urltest re-probes each member, in seconds.</summary>
    public int IntervalSeconds { get; set; } = 180;

    /// <summary>Latency tolerance (ms) before urltest switches members.</summary>
    public int ToleranceMs { get; set; } = 50;
}
