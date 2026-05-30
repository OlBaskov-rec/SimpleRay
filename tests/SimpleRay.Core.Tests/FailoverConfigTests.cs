using System.Linq;
using System.Text.Json.Nodes;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class FailoverConfigTests
{
    private static ProfileConfig Vless(string server) => ShareLinkParser.Parse(
        $"vless://b831381d-6324-4d53-ad4f-8cda48b30811@{server}:443" +
        "?security=reality&sni=www.microsoft.com&fp=chrome" +
        "&pbk=66dK2tcRJ1R6fc4cbukmnRBZPZh6tLMcRR58KCLt6AU&sid=ab12&type=tcp#n");

    private static JsonArray Outbounds(JsonObject cfg) => (JsonArray)cfg["outbounds"]!;

    private static JsonObject? ByTag(JsonObject cfg, string tag) =>
        Outbounds(cfg).OfType<JsonObject>().FirstOrDefault(o => (string?)o["tag"] == tag);

    [Fact]
    public void SingleProfile_HasNoGroup_ProxyTagIsTheServer()
    {
        var cfg = SingBoxConfigGenerator.Generate(Vless("a.example.com"), new RoutingSettings());

        var proxy = ByTag(cfg, "proxy");
        Assert.NotNull(proxy);
        Assert.Equal("vless", (string?)proxy!["type"]);            // direct server, not a group
        Assert.Equal("a.example.com", (string?)proxy["server"]);
        Assert.Null(ByTag(cfg, "proxy-1"));
    }

    [Fact]
    public void MultipleProfiles_UrlTest_BuildsGroupOverMembers()
    {
        var profiles = new[] { Vless("a.example.com"), Vless("b.example.com") };
        var routing = new RoutingSettings
        {
            Failover = new GroupSettings { Mode = FailoverMode.UrlTest, IntervalSeconds = 120, ToleranceMs = 40 },
        };

        var cfg = SingBoxConfigGenerator.Generate(profiles, routing);

        // Members exist with sequential tags and the real servers.
        Assert.Equal("a.example.com", (string?)ByTag(cfg, "proxy-1")!["server"]);
        Assert.Equal("b.example.com", (string?)ByTag(cfg, "proxy-2")!["server"]);

        // The group owns the "proxy" tag routing points at.
        var group = ByTag(cfg, "proxy")!;
        Assert.Equal("urltest", (string?)group["type"]);
        var members = ((JsonArray)group["outbounds"]!).Select(n => (string?)n).ToArray();
        Assert.Equal(new[] { "proxy-1", "proxy-2" }, members);
        Assert.Equal("120s", (string?)group["interval"]);
        Assert.Equal(40, (int)group["tolerance"]!);
        Assert.True((bool)group["interrupt_exist_connections"]!);

        // Routing still terminates at "proxy".
        Assert.Equal("proxy", (string?)cfg["route"]!["final"]);
    }

    [Fact]
    public void MultipleProfiles_Selector_HasDefaultMember()
    {
        var profiles = new[] { Vless("a.example.com"), Vless("b.example.com") };
        var routing = new RoutingSettings { Failover = new GroupSettings { Mode = FailoverMode.Selector } };

        var cfg = SingBoxConfigGenerator.Generate(profiles, routing);

        var group = ByTag(cfg, "proxy")!;
        Assert.Equal("selector", (string?)group["type"]);
        Assert.Equal("proxy-1", (string?)group["default"]);
    }
}
