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

    [Theory]
    [InlineData("hysteria2://pw@hy.example.com:443,20000-30000/?sni=hy.example.com#n")]
    [InlineData("hysteria2://pw@hy.example.com:443,20000-30000#n")]
    [InlineData("hysteria2://pw@hy.example.com:443/?mport=20000-30000#n")]
    [InlineData("hy2://pw@hy.example.com/?mport=20000-30000#n")] // port defaults to 443
    public void Hysteria2PortHopping_ParsesRanges(string link)
    {
        var p = ShareLinkParser.Parse(link);
        Assert.Equal("hy.example.com", p.Server);
        Assert.Equal(443, p.Port);
        Assert.Equal(new[] { "443", "20000-30000" }, p.PortRanges);
        Assert.Equal("pw", p.Password);
        Assert.Equal("n", p.Tag);
        Assert.Equal(link, p.Raw);
    }

    [Fact]
    public void Hysteria2PortHopping_StartsWithARange_AndReadsHopInterval()
    {
        var p = ShareLinkParser.Parse("hysteria2://pw@hy.example.com:20000-30000,40000/?hop_interval=45#n");
        Assert.Equal(20000, p.Port);
        Assert.Equal(new[] { "20000-30000", "40000" }, p.PortRanges);
        Assert.Equal("45s", p.HopInterval);
    }

    [Fact]
    public void Hysteria2PortHopping_Ipv6Host()
    {
        var p = ShareLinkParser.Parse("hysteria2://pw@[2001:db8::1]:443,20000-30000/#n");
        Assert.Equal("[2001:db8::1]", p.Server);
        Assert.Equal(new[] { "443", "20000-30000" }, p.PortRanges);
    }

    [Fact]
    public void Hysteria2SinglePort_HasNoRanges()
    {
        Assert.Null(ShareLinkParser.Parse("hysteria2://pw@hy.example.com:8443#n").PortRanges);
    }

    [Theory]
    [InlineData("hysteria2://pw@hy.example.com:443,30000-20000/#n")] // reversed
    [InlineData("hysteria2://pw@hy.example.com:443,70000/#n")]       // out of range
    [InlineData("hysteria2://pw@hy.example.com:443,1-2-3/#n")]
    [InlineData("hysteria2://pw@hy.example.com:443/?mport=abc#n")]
    public void Hysteria2BadPortSpec_IsRejected(string link)
    {
        Assert.False(ShareLinkParser.TryParse(link, out _));
    }

    [Fact]
    public void Hysteria2PortHopping_Outbound_UsesServerPorts()
    {
        var o = ProxyOutbound(ShareLinkParser.Parse(
            "hysteria2://pw@hy.example.com:443,20000-30000/?hop_interval=30s#n"));

        Assert.Null(o["server_port"]); // conflicts with server_ports in sing-box
        // sing-box rejects a bare "443" here ("bad port range"): every entry must be from:to.
        Assert.Equal(new[] { "443:443", "20000:30000" },
            o["server_ports"]!.AsArray().Select(n => (string?)n).ToArray());
        Assert.Equal("30s", (string?)o["hop_interval"]);
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
