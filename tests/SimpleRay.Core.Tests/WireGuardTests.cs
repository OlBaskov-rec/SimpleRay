using System.Linq;
using System.Text.Json.Nodes;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class WireGuardTests
{
    private const string Conf = """
        # Home WG
        [Interface]
        PrivateKey = SC7/JBU2Flu24hIOfr7GTnyciTSnFzBKTJ2+K68wB2w=
        Address = 10.66.66.2/32, fd42::2/128
        DNS = 1.1.1.1
        MTU = 1420

        [Peer]
        PublicKey = ERKwnts0P6QNOLZqJUQM7g723+bp7sNgjbeUm44pEFI=
        AllowedIPs = 0.0.0.0/0, ::/0
        Endpoint = wg.example.com:51820
        PersistentKeepalive = 25
        """;

    [Fact]
    public void Parses_StandardConf()
    {
        var p = WireGuardConfParser.Parse(Conf);

        Assert.Equal(ProxyProtocol.WireGuard, p.Protocol);
        Assert.Equal("wg.example.com", p.Server);
        Assert.Equal(51820, p.Port);
        Assert.Equal("Home WG", p.Tag);

        Assert.NotNull(p.Wg);
        Assert.Equal("SC7/JBU2Flu24hIOfr7GTnyciTSnFzBKTJ2+K68wB2w=", p.Wg!.PrivateKey);
        Assert.Equal("ERKwnts0P6QNOLZqJUQM7g723+bp7sNgjbeUm44pEFI=", p.Wg.PeerPublicKey);
        Assert.Equal(new[] { "10.66.66.2/32", "fd42::2/128" }, p.Wg.LocalAddresses.ToArray());
        Assert.Equal(new[] { "0.0.0.0/0", "::/0" }, p.Wg.AllowedIps.ToArray());
        Assert.Equal(1420, p.Wg.Mtu);
        Assert.Equal(25, p.Wg.PersistentKeepalive);
    }

    [Fact]
    public void ParseMany_DetectsConf()
    {
        var list = ShareLinkParser.ParseMany(Conf);
        Assert.Single(list);
        Assert.Equal(ProxyProtocol.WireGuard, list[0].Protocol);
    }

    [Fact]
    public void Generator_PutsWireGuardInEndpoints()
    {
        var p = WireGuardConfParser.Parse(Conf);
        var cfg = SingBoxConfigGenerator.Generate(p, new RoutingSettings());

        var endpoints = cfg["endpoints"] as JsonArray;
        Assert.NotNull(endpoints);
        var ep = (JsonObject)endpoints!.Single()!;
        Assert.Equal("wireguard", (string?)ep["type"]);
        Assert.Equal("proxy", (string?)ep["tag"]);
        Assert.Equal("SC7/JBU2Flu24hIOfr7GTnyciTSnFzBKTJ2+K68wB2w=", (string?)ep["private_key"]);

        var peer = (JsonObject)((JsonArray)ep["peers"]!).Single()!;
        Assert.Equal("wg.example.com", (string?)peer["address"]);
        Assert.Equal(51820, (int)peer["port"]!);
        Assert.Equal("ERKwnts0P6QNOLZqJUQM7g723+bp7sNgjbeUm44pEFI=", (string?)peer["public_key"]);

        // No proxy outbound; the proxy tag is the endpoint, routing still ends at "proxy".
        var outbounds = (JsonArray)cfg["outbounds"]!;
        Assert.DoesNotContain(outbounds.OfType<JsonObject>(), o => (string?)o["tag"] == "proxy");
        Assert.Equal("proxy", (string?)cfg["route"]!["final"]);
    }
}
