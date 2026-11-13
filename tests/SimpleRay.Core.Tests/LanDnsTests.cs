using System.Text.Json.Nodes;
using SimpleRay.Core.Config;
using SimpleRay.Core.Dns;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class LanDnsTests
{
    private static RoutingSettings Routing(RoutingMode mode) => new()
    {
        Mode = mode, BlockAds = false, DirectGeosite = new(), DirectGeoip = new(),
        DirectDomains = new() { "hk.local", "example.org" },
        ProxyDomains = new() { "hk.local", "example.com" },
    };

    private static JsonObject Generate(RoutingMode mode, GeneratorOptions? options = null) =>
        SingBoxConfigGenerator.Generate(
            ShareLinkParser.Parse("vless://uuid-x@example.com:443?security=tls#node"), Routing(mode), options);

    [Theory]
    [InlineData(RoutingMode.Global)]
    [InlineData(RoutingMode.Rule)]
    [InlineData(RoutingMode.Direct)]
    public void LanDns_IsFirstRule_WithAllPrivateReverseZones_AndDirectServer(RoutingMode mode)
    {
        var cfg = Generate(mode, new GeneratorOptions
        {
            LanDns = new LanDns(new[] { "hk.local", "office.lan" }, "172.17.0.2"),
        });
        var dns = cfg["dns"]!;
        var server = Assert.Single(dns["servers"]!.AsArray(), s => (string?)s!["tag"] == "lan")!;
        Assert.Equal("udp", (string?)server["type"]);
        Assert.Equal("172.17.0.2", (string?)server["server"]);
        Assert.False(server.AsObject().ContainsKey("detour"));
        var rule = dns["rules"]![0]!;
        Assert.Equal("lan", (string?)rule["server"]);
        var expected = new[] { "hk.local", "office.lan", "10.in-addr.arpa" }
            .Concat(Enumerable.Range(16, 16).Select(n => $"{n}.172.in-addr.arpa"))
            .Concat(new[] { "168.192.in-addr.arpa", "254.169.in-addr.arpa" });
        Assert.Equal(expected, rule["domain_suffix"]!.AsArray().Select(n => (string)n!));
        Assert.Equal("remote", (string?)dns["final"]);
        var route = cfg["route"]!["rules"]!.AsArray();
        Assert.Equal("direct", (string?)route[2]!["outbound"]);
        Assert.True((bool)route[2]!["ip_is_private"]!);
        Assert.Contains("172.16.0.0/12", cfg["inbounds"]![0]!["route_exclude_address"]!
            .AsArray().Select(n => (string)n!));
    }

    [Theory]
    [InlineData(RoutingMode.Global)]
    [InlineData(RoutingMode.Rule)]
    [InlineData(RoutingMode.Direct)]
    public void NullLanDns_PreservesEntireConfig_AndOriginalDns(RoutingMode mode)
    {
        var implicitConfig = Generate(mode);
        var explicitConfig = Generate(mode, new GeneratorOptions { LanDns = null });
        Assert.Equal(implicitConfig.ToJsonString(), explicitConfig.ToJsonString());
        // Independent pre-split-DNS snapshot: catches changes shared by both calls above.
        var expected = JsonNode.Parse("""
            {"servers":[{"type":"https","tag":"remote","server":"1.1.1.1","detour":"proxy"},
                        {"type":"https","tag":"local","server":"1.1.1.1"}],
             "final":"remote","strategy":"ipv4_only"}
            """)!;
        if (mode == RoutingMode.Rule)
            expected["rules"] = JsonNode.Parse("""
                [{"domain_suffix":["hk.local","example.com"],"server":"remote"},
                 {"domain_suffix":["hk.local","example.org"],"server":"local"}]
                """);
        Assert.Equal(expected.ToJsonString(), explicitConfig["dns"]!.ToJsonString());
    }
}
