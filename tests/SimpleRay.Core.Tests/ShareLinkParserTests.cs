using System.Text;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class ShareLinkParserTests
{
    [Fact]
    public void Vless_Reality_ParsesAllFields()
    {
        const string link =
            "vless://b831381d-6324-4d53-ad4f-8cda48b30811@example.com:443" +
            "?encryption=none&security=reality&sni=www.microsoft.com&fp=chrome" +
            "&pbk=PUBKEY123&sid=ab12&type=grpc&serviceName=grpcsvc&flow=xtls-rprx-vision" +
            "#My%20Node";

        var p = ShareLinkParser.Parse(link);

        Assert.Equal(ProxyProtocol.VLESS, p.Protocol);
        Assert.Equal("example.com", p.Server);
        Assert.Equal(443, p.Port);
        Assert.Equal("b831381d-6324-4d53-ad4f-8cda48b30811", p.Uuid);
        Assert.Equal("xtls-rprx-vision", p.Flow);
        Assert.Equal("grpc", p.Network);
        Assert.Equal("grpcsvc", p.ServiceName);
        Assert.Equal("My Node", p.Tag);

        Assert.NotNull(p.Tls);
        Assert.True(p.Tls!.Enabled);
        Assert.True(p.Tls.IsReality);
        Assert.Equal("www.microsoft.com", p.Tls.ServerName);
        Assert.Equal("chrome", p.Tls.Fingerprint);
        Assert.Equal("PUBKEY123", p.Tls.RealityPublicKey);
        Assert.Equal("ab12", p.Tls.RealityShortId);
    }

    [Fact]
    public void Vmess_Base64Json_Parses()
    {
        const string json =
            "{\"v\":\"2\",\"ps\":\"vmess-node\",\"add\":\"1.2.3.4\",\"port\":\"8443\"," +
            "\"id\":\"a3482e88-686a-4a58-8126-99c9df64b7bf\",\"aid\":\"0\",\"scy\":\"auto\"," +
            "\"net\":\"ws\",\"type\":\"none\",\"host\":\"cdn.example.com\",\"path\":\"/ray\"," +
            "\"tls\":\"tls\",\"sni\":\"cdn.example.com\",\"alpn\":\"h2,http/1.1\",\"fp\":\"chrome\"}";
        string link = "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var p = ShareLinkParser.Parse(link);

        Assert.Equal(ProxyProtocol.VMess, p.Protocol);
        Assert.Equal("1.2.3.4", p.Server);
        Assert.Equal(8443, p.Port);
        Assert.Equal("a3482e88-686a-4a58-8126-99c9df64b7bf", p.Uuid);
        Assert.Equal(0, p.AlterId);
        Assert.Equal("auto", p.Method);
        Assert.Equal("ws", p.Network);
        Assert.Equal("/ray", p.Path);
        Assert.Equal("cdn.example.com", p.Host);
        Assert.Equal("vmess-node", p.Tag);

        Assert.NotNull(p.Tls);
        Assert.True(p.Tls!.Enabled);
        Assert.Equal("cdn.example.com", p.Tls.ServerName);
        Assert.Equal(new[] { "h2", "http/1.1" }, p.Tls.Alpn);
    }

    [Fact]
    public void Trojan_DefaultsToTls()
    {
        const string link = "trojan://secretpass@trojan.example.com:443?type=ws&path=/tj#TJ";

        var p = ShareLinkParser.Parse(link);

        Assert.Equal(ProxyProtocol.Trojan, p.Protocol);
        Assert.Equal("trojan.example.com", p.Server);
        Assert.Equal(443, p.Port);
        Assert.Equal("secretpass", p.Password);
        Assert.Equal("ws", p.Network);
        Assert.Equal("/tj", p.Path);
        Assert.NotNull(p.Tls);
        Assert.True(p.Tls!.Enabled);
    }

    [Fact]
    public void Shadowsocks_Sip002_Parses()
    {
        string userInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:mypassword"));
        string link = $"ss://{userInfo}@ss.example.com:8388#SS-Node";

        var p = ShareLinkParser.Parse(link);

        Assert.Equal(ProxyProtocol.Shadowsocks, p.Protocol);
        Assert.Equal("ss.example.com", p.Server);
        Assert.Equal(8388, p.Port);
        Assert.Equal("aes-256-gcm", p.Method);
        Assert.Equal("mypassword", p.Password);
        Assert.Equal("SS-Node", p.Tag);
    }

    [Fact]
    public void Shadowsocks_Legacy_Parses()
    {
        string payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("chacha20-ietf-poly1305:pw@1.2.3.4:8388"));
        string link = $"ss://{payload}#Legacy";

        var p = ShareLinkParser.Parse(link);

        Assert.Equal("1.2.3.4", p.Server);
        Assert.Equal(8388, p.Port);
        Assert.Equal("chacha20-ietf-poly1305", p.Method);
        Assert.Equal("pw", p.Password);
    }

    [Fact]
    public void ParseMany_ExtractsValidSkipsInvalid()
    {
        string text = string.Join("\n",
            "vless://uuid-1@a.com:443?security=tls#A",
            "garbage line",
            "trojan://pw@b.com:443#B",
            "");

        var list = ShareLinkParser.ParseMany(text);

        Assert.Equal(2, list.Count);
        Assert.Equal(ProxyProtocol.VLESS, list[0].Protocol);
        Assert.Equal(ProxyProtocol.Trojan, list[1].Protocol);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("vless://no-host-or-port")]
    [InlineData("vmess://not-valid-base64!!!")]
    public void TryParse_ReturnsFalse_OnBadInput(string link)
    {
        Assert.False(ShareLinkParser.TryParse(link, out var p));
        Assert.Null(p);
    }

    [Theory]
    [InlineData("this is not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    public void TryParse_ReturnsFalse_OnVmessWithNonObjectPayload(string payload)
    {
        string link = "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        Assert.False(ShareLinkParser.TryParse(link, out var p));
        Assert.Null(p);
    }

    [Fact]
    public void Vmess_NonStringJsonFields_AreToleratedNotFatal()
    {
        // port as a number, path as an object, host null, aid as a number — all seen
        // in the wild; none of them may throw.
        const string json =
            "{\"add\":\"1.2.3.4\",\"port\":8443,\"id\":\"a3482e88-686a-4a58-8126-99c9df64b7bf\"," +
            "\"aid\":0,\"path\":{},\"host\":null}";
        string link = "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        Assert.True(ShareLinkParser.TryParse(link, out var p));
        Assert.Equal("1.2.3.4", p!.Server);
        Assert.Equal(8443, p.Port);
        Assert.Equal(0, p.AlterId);
        Assert.Null(p.Path);
        Assert.Null(p.Host);
    }
}
