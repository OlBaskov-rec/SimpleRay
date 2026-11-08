using System.Linq;
using System.Text.Json.Nodes;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class Hysteria2SocksTests
{
    private static JsonObject ProxyOutbound(ProfileConfig p) =>
        SingBoxConfigGenerator.Generate(p, new RoutingSettings())["outbounds"]!.AsArray()[0]!.AsObject();

    // ---- Hysteria2 ------------------------------------------------------------------

    [Fact]
    public void Hysteria2Link_ParsesAuthObfsAndTls()
    {
        var p = ShareLinkParser.Parse(
            "hysteria2://s3cr%40t@hy.example.com:8443/?sni=cdn.example.com&insecure=1&obfs=salamander" +
            "&obfs-password=ob&alpn=h3&upmbps=50&downmbps=200#HY%20node");

        Assert.Equal(ProxyProtocol.Hysteria2, p.Protocol);
        Assert.Equal("hy.example.com", p.Server);
        Assert.Equal(8443, p.Port);
        Assert.Equal("s3cr@t", p.Password);
        Assert.Equal("salamander", p.ObfsType);
        Assert.Equal("ob", p.ObfsPassword);
        Assert.Equal(50, p.UpMbps);
        Assert.Equal(200, p.DownMbps);
        Assert.Equal("HY node", p.Tag);
        Assert.True(p.Tls!.Enabled);
        Assert.Equal("cdn.example.com", p.Tls.ServerName);
        Assert.True(p.Tls.AllowInsecure);
        Assert.Equal(new[] { "h3" }, p.Tls.Alpn);
        Assert.True(p.IsUdpBased);
    }

    [Fact]
    public void Hy2Alias_DefaultsPortAndSni()
    {
        var p = ShareLinkParser.Parse("hy2://pw@hy.example.com#n");
        Assert.Equal(ProxyProtocol.Hysteria2, p.Protocol);
        Assert.Equal(443, p.Port);
        Assert.Equal("hy.example.com", p.Tls!.ServerName);
        Assert.Null(p.ObfsType);
    }

    [Fact]
    public void Hysteria2PortHopping_IsRejected()
    {
        Assert.False(ShareLinkParser.TryParse("hysteria2://pw@hy.example.com:443,20000-30000/#n", out _));
    }

    [Fact]
    public void Hysteria2_Outbound_IsQuicTlsWithoutUtlsOrTransport()
    {
        var p = ShareLinkParser.Parse(
            "hysteria2://pw@hy.example.com:8443/?sni=cdn.example.com&obfs=salamander&obfs-password=ob#n");
        p.Tls!.Fingerprint = "chrome"; // must not leak into a QUIC outbound

        var o = ProxyOutbound(p);

        Assert.Equal("hysteria2", (string?)o["type"]);
        Assert.Equal("hy.example.com", (string?)o["server"]);
        Assert.Equal(8443, (int)o["server_port"]!);
        Assert.Equal("pw", (string?)o["password"]);
        Assert.Equal("salamander", (string?)o["obfs"]!["type"]);
        Assert.Equal("ob", (string?)o["obfs"]!["password"]);
        Assert.True((bool)o["tls"]!["enabled"]!);
        Assert.Equal("cdn.example.com", (string?)o["tls"]!["server_name"]);
        Assert.Null(o["tls"]!["utls"]);
        Assert.Null(o["transport"]);
    }

    // ---- SOCKS ----------------------------------------------------------------------

    [Theory]
    [InlineData("socks://dXNlcjpwYXNz@s.example.com:1080#S", "user", "pass")]   // v2rayN: base64(user:pass)
    [InlineData("socks5://user:pa%3Ass@s.example.com:1080#S", "user", "pa:ss")]
    [InlineData("socks5://bob@s.example.com:1080#S", "bob", null)]
    [InlineData("socks://s.example.com:1080#S", null, null)]
    public void SocksLinks_Parse(string link, string? user, string? pass)
    {
        var p = ShareLinkParser.Parse(link);
        Assert.Equal(ProxyProtocol.Socks, p.Protocol);
        Assert.Equal("s.example.com", p.Server);
        Assert.Equal(1080, p.Port);
        Assert.Equal(user, p.Username);
        Assert.Equal(pass, p.Password);
        Assert.False(p.IsUdpBased);
    }

    [Fact]
    public void Socks_Outbound_IsPlainSocks5()
    {
        var o = ProxyOutbound(ShareLinkParser.Parse("socks5://user:pass@s.example.com:1080#S"));
        Assert.Equal("socks", (string?)o["type"]);
        Assert.Equal("5", (string?)o["version"]);
        Assert.Equal("user", (string?)o["username"]);
        Assert.Equal("pass", (string?)o["password"]);
        Assert.Null(o["tls"]);
        Assert.Null(o["transport"]);
    }

    [Fact]
    public void XrayJson_SocksOutbound_IsMapped()
    {
        const string json = """
        [ { "remarks": "Socks JSON", "outbounds": [ { "protocol": "socks",
            "settings": { "servers": [ { "address": "s.example.com", "port": 1080,
              "users": [ { "user": "u", "pass": "p" } ] } ] } } ] } ]
        """;
        var p = Assert.Single(SubscriptionParser.Parse(json));
        Assert.Equal(ProxyProtocol.Socks, p.Protocol);
        Assert.Equal("Socks JSON", p.Tag);
        Assert.Equal("u", p.Username);
        Assert.Equal("p", p.Password);
    }

    [Fact]
    public void Subscription_MixesNewProtocolsWithOld()
    {
        var body = "hy2://pw@hy.example.com:443#H\nsocks5://u:p@s.example.com:1080#S\ntrojan://pw@b.example.com:8443#B";
        var protocols = SubscriptionParser.Parse(body).Select(p => p.Protocol).ToArray();
        Assert.Equal(new[] { ProxyProtocol.Hysteria2, ProxyProtocol.Socks, ProxyProtocol.Trojan }, protocols);
    }
}
