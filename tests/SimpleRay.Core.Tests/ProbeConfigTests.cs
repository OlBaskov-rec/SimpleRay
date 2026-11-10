using System;
using System.Linq;
using System.Text.Json.Nodes;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

/// <summary>The throwaway "ping through the server" config must stay local, authenticated and per-server.</summary>
public class ProbeConfigTests
{
    private static readonly ProfileConfig[] Servers =
    {
        ShareLinkParser.Parse("trojan://pw@a.example.com:8443#A"),
        ShareLinkParser.Parse("hy2://pw@b.example.com:443#B"),
    };

    private static JsonObject Probe() =>
        SingBoxConfigGenerator.GenerateProbe(Servers, new[] { 41001, 41002 }, "u", "p");

    [Fact]
    public void Inbounds_AreLoopbackOnly_AndRequireCredentials()
    {
        var inbounds = Probe()["inbounds"]!.AsArray().Select(n => n!.AsObject()).ToList();
        Assert.Equal(2, inbounds.Count);
        Assert.All(inbounds, i =>
        {
            Assert.Equal("mixed", (string?)i["type"]);
            Assert.Equal("127.0.0.1", (string?)i["listen"]);
            var user = i["users"]!.AsArray().Single()!;
            Assert.Equal("u", (string?)user["username"]);
            Assert.Equal("p", (string?)user["password"]);
        });
        Assert.Equal(new[] { 41001, 41002 }, inbounds.Select(i => (int)i["listen_port"]!));
    }

    [Fact]
    public void EachInbound_RoutesToItsOwnServer_AndNothingElseLeaks()
    {
        var cfg = Probe();
        var outbounds = cfg["outbounds"]!.AsArray().Select(n => n!.AsObject()).ToList();
        Assert.Equal(new[] { "trojan", "hysteria2" }, outbounds.Select(o => (string?)o["type"]));
        Assert.DoesNotContain(outbounds, o => (string?)o["type"] == "direct");

        var rules = cfg["route"]!["rules"]!.AsArray().Select(n => n!.AsObject()).ToList();
        Assert.Equal("probe-in-1", (string?)rules[0]["inbound"]![0]);
        Assert.Equal("probe-1", (string?)rules[0]["outbound"]);
        Assert.Equal("probe-in-2", (string?)rules[1]["inbound"]![0]);
        Assert.Equal("probe-2", (string?)rules[1]["outbound"]);
        Assert.Equal("reject", (string?)rules[^1]["action"]);
        Assert.True((bool)cfg["route"]!["auto_detect_interface"]!);
    }

    [Fact]
    public void HasNoTun_SoItNeverTouchesSystemRouting()
    {
        var cfg = Probe();
        Assert.DoesNotContain(cfg["inbounds"]!.AsArray(), n => (string?)n!["type"] == "tun");
    }

    [Fact]
    public void WireGuard_GoesToEndpoints()
    {
        var wg = new ProfileConfig
        {
            Protocol = ProxyProtocol.WireGuard, Server = "wg.example.com", Port = 51820,
            Wg = new WireGuardSettings { PrivateKey = "k", PeerPublicKey = "p", LocalAddresses = { "10.0.0.2/32" } },
        };
        var cfg = SingBoxConfigGenerator.GenerateProbe(new[] { wg }, new[] { 41003 }, "u", "p");
        Assert.Equal("probe-1", (string?)cfg["endpoints"]![0]!["tag"]);
    }

    [Fact]
    public void RejectsMismatchedPorts_AndMissingCredentials()
    {
        Assert.Throws<ArgumentException>(() => SingBoxConfigGenerator.GenerateProbe(Servers, new[] { 1 }, "u", "p"));
        Assert.Throws<ArgumentException>(() => SingBoxConfigGenerator.GenerateProbe(Servers, new[] { 1, 2 }, "", "p"));
    }
}
