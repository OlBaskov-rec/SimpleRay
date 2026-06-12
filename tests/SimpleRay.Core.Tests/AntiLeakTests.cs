using System.Linq;
using System.Text.Json.Nodes;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

/// <summary>
/// Leak-prevention invariants: no local app can grab the tunnel as a proxy, LAN/DNS
/// can't leak around it, and per-app rules take precedence over geo routing. These must
/// hold in every routing mode and for multi-server groups.
/// </summary>
public class AntiLeakTests
{
    private static ProfileConfig Vless(string host = "example.com") => ShareLinkParser.Parse(
        $"vless://uuid-x@{host}:443?security=reality&sni=www.microsoft.com&fp=chrome" +
        "&pbk=PBK&sid=ab12&type=tcp#node");

    public static IEnumerable<object[]> AllModes() => new[]
    {
        new object[] { RoutingMode.Global },
        new object[] { RoutingMode.Rule },
        new object[] { RoutingMode.Direct },
    };

    [Theory]
    [MemberData(nameof(AllModes))]
    public void NoUnauthenticatedInbound_InAnyMode(RoutingMode mode)
    {
        var cfg = SingBoxConfigGenerator.Generate(Vless(), new RoutingSettings { Mode = mode });

        var inbounds = cfg["inbounds"]!.AsArray();
        // The ONLY ingress is the TUN device; no socks/http/mixed proxy a local app could abuse.
        Assert.All(inbounds, n => Assert.DoesNotContain((string?)n!["type"], new[] { "socks", "http", "mixed" }));
        var tun = inbounds.Single(n => (string?)n!["type"] == "tun")!.AsObject();
        Assert.True((bool)tun["strict_route"]!);  // strict_route blocks traffic leaking around the tunnel
        Assert.True((bool)tun["auto_route"]!);
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void DnsHijackAndPrivateBypass_ArePresent(RoutingMode mode)
    {
        var rules = SingBoxConfigGenerator.Generate(Vless(), new RoutingSettings { Mode = mode })
            ["route"]!["rules"]!.AsArray();

        // Apps can't leak DNS (hijacked) and LAN stays local (not tunneled).
        Assert.Contains(rules, r => (string?)r!["action"] == "hijack-dns");
        Assert.Contains(rules, r => (bool?)r!["ip_is_private"] == true && (string?)r["outbound"] == "direct");
    }

    [Fact]
    public void PerAppRules_TakePrecedenceOverGeo_AndMapCorrectly()
    {
        var routing = new RoutingSettings
        {
            Mode = RoutingMode.Rule,
            DirectGeoip = new() { "geoip-ru" },
            ProxyProcesses = new() { "chrome" },
            DirectProcesses = new() { "Telegram" },
        };

        var rules = SingBoxConfigGenerator.Generate(Vless(), routing)["route"]!["rules"]!.AsArray();

        int IndexOf(System.Func<JsonNode?, bool> pred) =>
            rules.ToList().FindIndex(r => pred(r));

        int directProc = IndexOf(r => r!["process_name"] is JsonArray a
            && a.Any(x => (string?)x == "Telegram.exe") && (string?)r["outbound"] == "direct");
        int proxyProc = IndexOf(r => r!["process_name"] is JsonArray a
            && a.Any(x => (string?)x == "chrome.exe") && (string?)r["outbound"] == "proxy");
        int geoDirect = IndexOf(r => r!["rule_set"] is JsonArray a && a.Any(x => (string?)x == "geoip-ru"));

        Assert.True(directProc >= 0 && proxyProc >= 0 && geoDirect >= 0);
        // sing-box is first-match: per-app rules must come before the geo fallthrough.
        Assert.True(directProc < geoDirect, "direct-process rule must precede geo rule");
        Assert.True(proxyProc < geoDirect, "proxy-process rule must precede geo rule");
    }

    [Fact]
    public void FailoverGroup_StillHasNoInboundProxy_AndProxyTagIsGroup()
    {
        var profiles = new[] { Vless("a.com"), Vless("b.com") };
        var cfg = SingBoxConfigGenerator.Generate(profiles, new RoutingSettings());

        Assert.All(cfg["inbounds"]!.AsArray(),
            n => Assert.DoesNotContain((string?)n!["type"], new[] { "socks", "http", "mixed" }));

        // The single egress the routing points at ("proxy") is the failover group.
        var proxy = cfg["outbounds"]!.AsArray().OfType<JsonObject>().Single(o => (string?)o["tag"] == "proxy");
        Assert.Equal("urltest", (string?)proxy["type"]);
        Assert.Equal("proxy", (string?)cfg["route"]!["final"]);
    }
}
