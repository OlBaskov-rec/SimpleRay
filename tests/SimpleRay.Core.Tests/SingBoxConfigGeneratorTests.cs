using System.Text.Json.Nodes;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class SingBoxConfigGeneratorTests
{
    private static ProfileConfig VlessReality() => ShareLinkParser.Parse(
        "vless://uuid-x@example.com:443?security=reality&sni=www.microsoft.com&fp=chrome" +
        "&pbk=PBK&sid=ab12&type=grpc&serviceName=svc&flow=xtls-rprx-vision#node");

    [Fact]
    public void Tun_Inbound_HasNoOpenLoopbackProxy()
    {
        var cfg = SingBoxConfigGenerator.Generate(VlessReality(), new RoutingSettings());

        var inbounds = cfg["inbounds"]!.AsArray();
        Assert.Single(inbounds);
        var tun = inbounds[0]!.AsObject();
        Assert.Equal("tun", (string?)tun["type"]);
        Assert.True((bool)tun["auto_route"]!);
        Assert.True((bool)tun["strict_route"]!);
        // No socks/http/mixed inbound exists => loopback-proxy vulnerability cannot occur.
        Assert.DoesNotContain(inbounds, n =>
            (string?)n!["type"] is "socks" or "http" or "mixed");
    }

    [Fact]
    public void Vless_Outbound_EncodesRealityAndGrpc()
    {
        var cfg = SingBoxConfigGenerator.Generate(VlessReality(), new RoutingSettings());

        var proxy = cfg["outbounds"]!.AsArray()[0]!.AsObject();
        Assert.Equal("vless", (string?)proxy["type"]);
        Assert.Equal("proxy", (string?)proxy["tag"]);
        Assert.Equal("example.com", (string?)proxy["server"]);
        Assert.Equal(443, (int)proxy["server_port"]!);
        Assert.Equal("xtls-rprx-vision", (string?)proxy["flow"]);

        var tls = proxy["tls"]!.AsObject();
        Assert.True((bool)tls["enabled"]!);
        Assert.Equal("www.microsoft.com", (string?)tls["server_name"]);
        Assert.Equal("chrome", (string?)tls["utls"]!["fingerprint"]);
        Assert.Equal("PBK", (string?)tls["reality"]!["public_key"]);

        var transport = proxy["transport"]!.AsObject();
        Assert.Equal("grpc", (string?)transport["type"]);
        Assert.Equal("svc", (string?)transport["service_name"]);
    }

    [Fact]
    public void RuleMode_AddsDirectRuleSetsAndFinalProxy()
    {
        var routing = new RoutingSettings
        {
            Mode = RoutingMode.Rule,
            DirectGeosite = new() { "geosite-private", "geosite-ru" },
            DirectGeoip = new() { "geoip-ru" },
            BlockAds = true,
        };

        var route = SingBoxConfigGenerator.Generate(VlessReality(), routing)["route"]!.AsObject();
        Assert.Equal("proxy", (string?)route["final"]);

        var ruleSetTags = route["rule_set"]!.AsArray().Select(n => (string?)n!["tag"]).ToList();
        Assert.Contains("geosite-ru", ruleSetTags);
        Assert.Contains("geoip-ru", ruleSetTags);
        Assert.Contains("geosite-category-ads-all", ruleSetTags);

        // Ads rule rejects; a direct rule routes geo sets to direct.
        var rules = route["rules"]!.AsArray();
        Assert.Contains(rules, r => (string?)r!["action"] == "reject");
        Assert.Contains(rules, r => r!["rule_set"] is JsonArray a
            && a.Any(x => (string?)x == "geoip-ru")
            && (string?)r["outbound"] == "direct");
    }

    [Fact]
    public void DirectMode_FinalIsDirect()
    {
        var route = SingBoxConfigGenerator.Generate(
            VlessReality(), new RoutingSettings { Mode = RoutingMode.Direct })["route"]!.AsObject();
        Assert.Equal("direct", (string?)route["final"]);
    }

    [Fact]
    public void ProcessRules_AppendExeAndRoute()
    {
        var routing = new RoutingSettings
        {
            ProxyProcesses = new() { "chrome" },
            DirectProcesses = new() { "Telegram.exe" },
        };

        var rules = SingBoxConfigGenerator.Generate(VlessReality(), routing)["route"]!["rules"]!.AsArray();

        Assert.Contains(rules, r => r!["process_name"] is JsonArray a
            && a.Any(x => (string?)x == "chrome.exe") && (string?)r["outbound"] == "proxy");
        Assert.Contains(rules, r => r!["process_name"] is JsonArray a
            && a.Any(x => (string?)x == "Telegram.exe") && (string?)r["outbound"] == "direct");
    }

    [Fact]
    public void Shadowsocks_Outbound_HasMethodAndPassword()
    {
        var p = ShareLinkParser.Parse(
            "ss://" + Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("aes-256-gcm:pw")) + "@h.com:8388#n");

        var proxy = SingBoxConfigGenerator.Generate(p, new RoutingSettings())["outbounds"]!
            .AsArray()[0]!.AsObject();

        Assert.Equal("shadowsocks", (string?)proxy["type"]);
        Assert.Equal("aes-256-gcm", (string?)proxy["method"]);
        Assert.Equal("pw", (string?)proxy["password"]);
        Assert.Null(proxy["tls"]);
    }
}
