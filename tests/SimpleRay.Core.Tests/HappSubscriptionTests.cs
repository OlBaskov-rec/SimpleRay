using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

/// <summary>Happ-style subscriptions: metadata headers / body lines, Xray JSON bodies, happ:// links.</summary>
public class HappSubscriptionTests
{
    private const string Link = "trojan://pw@b.example.com:8443#B";

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private static KeyValuePair<string, string> H(string k, string v) => new(k, v);

    // ---- metadata ---------------------------------------------------------------

    [Fact]
    public void Headers_FillMetadata()
    {
        var c = SubscriptionParser.ParseContent(B64(Link), new[]
        {
            H("Profile-Title", "base64:" + B64("Мой VPN")),
            H("subscription-userinfo", "upload=100; download=2153701362; total=10737418240; expire=1790951622"),
            H("profile-update-interval", "12"),
            H("support-url", "https://t.me/support"),
            H("profile-web-page-url", "https://example.com"),
            H("announce", "Hello"),
        });

        Assert.Single(c.Profiles);
        var m = c.Metadata;
        Assert.Equal("Мой VPN", m.Title);
        Assert.Equal(100, m.Upload);
        Assert.Equal(2153701362, m.Download);
        Assert.Equal(2153701462, m.Used);
        Assert.Equal(10737418240, m.Total);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790951622), m.Expire);
        Assert.Equal(12, m.UpdateIntervalHours);
        Assert.Equal(TimeSpan.FromHours(12), m.UpdateInterval);
        Assert.Equal("https://t.me/support", m.SupportUrl);
        Assert.Equal("https://example.com/", m.WebPageUrl);
        Assert.Equal("Hello", m.Announce);
    }

    [Fact]
    public void ZeroTotalAndExpire_MeanUnlimitedAndNever()
    {
        var m = SubscriptionParser.ParseContent(Link,
            new[] { H("subscription-userinfo", "upload=0; download=5; total=0; expire=0") }).Metadata;
        Assert.Null(m.Total);
        Assert.Null(m.Expire);
        Assert.Equal(5, m.Used);
    }

    [Fact]
    public void BodyMetadataLines_AreParsed_AndNotTreatedAsLinks()
    {
        var body =
            "#profile-title: Name VPN\n" +
            "#subscription-userinfo: upload=0; download=10; total=20; expire=1790951622\n" +
            "#profile-update-interval: 3\n" +
            "#announce: base64:" + B64("Happ the best!") + "\n" +
            "#some-unknown-key: ignored\n" +
            Link;

        var c = SubscriptionParser.ParseContent(body);

        Assert.Single(c.Profiles);
        Assert.Equal("Name VPN", c.Metadata.Title);
        Assert.Equal(20, c.Metadata.Total);
        Assert.Equal(3, c.Metadata.UpdateIntervalHours);
        Assert.Equal("Happ the best!", c.Metadata.Announce);
    }

    [Fact]
    public void Base64Body_WithMetadataLinesInside_Works()
    {
        var c = SubscriptionParser.ParseContent(B64("#profile-title: Inside\n" + Link));
        Assert.Single(c.Profiles);
        Assert.Equal("Inside", c.Metadata.Title);
    }

    [Fact]
    public void Header_WinsOverBody()
    {
        var c = SubscriptionParser.ParseContent("#profile-title: Body\n" + Link,
            new[] { H("profile-title", "Header") });
        Assert.Equal("Header", c.Metadata.Title);
    }

    [Fact]
    public void EncryptTagHeader_ReportsEncrypted()
    {
        var c = SubscriptionParser.ParseContent("AAAA", new[] { H("encrypt-tag", "abc=") });
        Assert.True(c.Encrypted);
        Assert.Empty(c.Profiles);
    }

    [Fact]
    public void NonHttpSupportUrl_IsDropped()
    {
        var m = SubscriptionParser.ParseContent(Link, new[] { H("support-url", "javascript:alert(1)") }).Metadata;
        Assert.Null(m.SupportUrl);
    }

    [Fact]
    public void UpdateDue_HonoursIntervalAndDefault()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var m = new SubscriptionMetadata { UpdateIntervalHours = 2 };
        Assert.True(m.IsUpdateDue(null, now));
        Assert.False(m.IsUpdateDue(now.AddHours(-1), now));
        Assert.True(m.IsUpdateDue(now.AddHours(-2), now));

        var dflt = new SubscriptionMetadata();
        Assert.False(dflt.IsUpdateDue(now.AddHours(-23), now));
        Assert.True(dflt.IsUpdateDue(now.AddHours(-24), now));
    }

    // ---- placeholder "servers" ------------------------------------------------------

    [Fact]
    public void ProviderStub_IsNotAProfile_ButANotice()
    {
        // What a provider sends to a client it doesn't serve.
        var c = SubscriptionParser.ParseContent(B64("vless://00000000-0000-0000-0000-000000000000@0.0.0.0:1#App%20not%20supported"));
        Assert.Empty(c.Profiles);
        Assert.Equal(new[] { "App not supported" }, c.Metadata.Notices);
    }

    [Theory]
    [InlineData("trojan://pw@real.example.com:1#Port one")]
    [InlineData("trojan://pw@127.0.0.1:443#Loopback")]
    [InlineData("trojan://pw@localhost:443#Localhost")]
    [InlineData("vless://00000000-0000-0000-0000-000000000000@real.example.com:443#Zero uuid")]
    public void PlaceholderShapes_AreDetected(string link)
    {
        var p = ShareLinkParser.Parse(link);
        Assert.True(SubscriptionParser.IsPlaceholder(p));
    }

    [Fact]
    public void Stubs_AreSplitFromRealServers_AndDeduped()
    {
        var body = string.Join("\n",
            "trojan://pw@0.0.0.0:1#Subscription%20expires%20soon",
            Link,
            "trojan://pw@0.0.0.0:1#Subscription%20expires%20soon");
        var c = SubscriptionParser.ParseContent(body);

        var real = Assert.Single(c.Profiles);
        Assert.Equal("b.example.com", real.Server);
        Assert.Equal(new[] { "Subscription expires soon" }, c.Metadata.Notices);
    }

    [Fact]
    public void RealServers_HaveNoNotices()
    {
        Assert.Null(SubscriptionParser.ParseContent(Link).Metadata.Notices);
        Assert.False(SubscriptionParser.IsPlaceholder(ShareLinkParser.Parse(Link)));
    }

    // ---- Xray JSON body -----------------------------------------------------------

    private const string JsonBody = """
    [
      {
        "remarks": "🇩🇪 Germany",
        "outbounds": [
          {
            "tag": "proxy",
            "protocol": "vless",
            "settings": { "vnext": [ { "address": "de.example.com", "port": 443,
              "users": [ { "id": "uuid-1", "flow": "xtls-rprx-vision", "encryption": "none" } ] } ] },
            "streamSettings": { "network": "tcp", "security": "reality",
              "realitySettings": { "serverName": "www.microsoft.com", "publicKey": "PBK",
                "shortId": "ab12", "fingerprint": "chrome" } }
          },
          { "tag": "direct", "protocol": "freedom" },
          { "tag": "block", "protocol": "blackhole" }
        ]
      },
      {
        "remarks": "NL ws",
        "outbounds": [
          { "tag": "direct", "protocol": "freedom" },
          {
            "tag": "proxy",
            "protocol": "trojan",
            "settings": { "servers": [ { "address": "nl.example.com", "port": "8443", "password": "pw" } ] },
            "streamSettings": { "network": "ws", "security": "tls",
              "wsSettings": { "path": "/ws", "headers": { "Host": "cdn.example.com" } },
              "tlsSettings": { "serverName": "nl.example.com", "alpn": ["h2", "http/1.1"], "allowInsecure": false } }
          }
        ]
      },
      {
        "remarks": "xhttp only",
        "outbounds": [
          { "tag": "proxy", "protocol": "vless",
            "settings": { "vnext": [ { "address": "x.example.com", "port": 443, "users": [ { "id": "u" } ] } ] },
            "streamSettings": { "network": "xhttp", "security": "tls" } }
        ]
      },
      {
        "remarks": "SS",
        "outbounds": [
          { "protocol": "shadowsocks",
            "settings": { "servers": [ { "address": "ss.example.com", "port": 8388,
              "method": "chacha20-ietf-poly1305", "password": "sspw" } ] } }
        ]
      }
    ]
    """;

    [Fact]
    public void XrayJsonArray_MapsOutbounds()
    {
        var c = SubscriptionParser.ParseContent(JsonBody);

        Assert.Equal(3, c.Profiles.Count);
        Assert.Equal(1, c.Skipped); // xhttp

        var de = c.Profiles[0];
        Assert.Equal("🇩🇪 Germany", de.Tag);
        Assert.Equal(ProxyProtocol.VLESS, de.Protocol);
        Assert.Equal("de.example.com", de.Server);
        Assert.Equal(443, de.Port);
        Assert.Equal("uuid-1", de.Uuid);
        Assert.Equal("xtls-rprx-vision", de.Flow);
        Assert.Equal("tcp", de.Network);
        Assert.NotNull(de.Tls);
        Assert.True(de.Tls!.IsReality);
        Assert.Equal("PBK", de.Tls.RealityPublicKey);
        Assert.Equal("ab12", de.Tls.RealityShortId);
        Assert.Equal("www.microsoft.com", de.Tls.ServerName);
        Assert.Equal("chrome", de.Tls.Fingerprint);
        Assert.StartsWith("xray-json:", de.Raw);

        var nl = c.Profiles[1];
        Assert.Equal(ProxyProtocol.Trojan, nl.Protocol);
        Assert.Equal(8443, nl.Port); // port as a string
        Assert.Equal("pw", nl.Password);
        Assert.Equal("ws", nl.Network);
        Assert.Equal("/ws", nl.Path);
        Assert.Equal("cdn.example.com", nl.Host);
        Assert.Equal(new[] { "h2", "http/1.1" }, nl.Tls!.Alpn);
        Assert.False(nl.Tls.IsReality);

        var ss = c.Profiles[2];
        Assert.Equal(ProxyProtocol.Shadowsocks, ss.Protocol);
        Assert.Equal("chacha20-ietf-poly1305", ss.Method);
        Assert.Equal("sspw", ss.Password);
        Assert.Null(ss.Tls);
    }

    [Fact]
    public void XrayJson_RawIsStableAcrossParses()
    {
        var a = SubscriptionParser.Parse(JsonBody).Select(p => p.Raw).ToArray();
        var b = SubscriptionParser.Parse(JsonBody).Select(p => p.Raw).ToArray();
        Assert.Equal(a, b);
        Assert.Equal(a.Length, a.Distinct().Count());
    }

    [Fact]
    public void XrayJson_SingleObjectAndBase64_Work()
    {
        const string one = """
        { "remarks": "One", "outbounds": [ { "protocol": "vmess",
          "settings": { "vnext": [ { "address": "v.example.com", "port": 443,
            "users": [ { "id": "vm-id", "alterId": 0, "security": "auto" } ] } ] },
          "streamSettings": { "network": "grpc", "security": "tls",
            "grpcSettings": { "serviceName": "svc" }, "tlsSettings": { "serverName": "v.example.com" } } } ] }
        """;
        var p = Assert.Single(SubscriptionParser.Parse(B64(one)));
        Assert.Equal("One", p.Tag);
        Assert.Equal(ProxyProtocol.VMess, p.Protocol);
        Assert.Equal("grpc", p.Network);
        Assert.Equal("svc", p.ServiceName);
    }

    [Fact]
    public void XrayJson_Malformed_ReturnsEmpty()
    {
        Assert.Empty(SubscriptionParser.Parse("[ { \"remarks\": "));
        Assert.Empty(SubscriptionParser.Parse("[ { \"outbounds\": [ { \"protocol\": \"vless\", \"settings\": {} } ] } ]"));
    }

    // ---- links ----------------------------------------------------------------------

    [Theory]
    [InlineData("https://sub.example.com/abc", "https://sub.example.com/abc")]
    [InlineData("  https://sub.example.com/abc  ", "https://sub.example.com/abc")]
    [InlineData("happ://add/https://sub.example.com/abc?x=1", "https://sub.example.com/abc?x=1")]
    [InlineData("happ://add/https%3A%2F%2Fsub.example.com%2Fabc", "https://sub.example.com/abc")]
    [InlineData("HAPP://ADD/https://sub.example.com/abc", "https://sub.example.com/abc")]
    // Other clients' import links carrying the URL in the clear.
    [InlineData("throne://subscribe?url=https://sub.example.com/AbCdEf0123456789",
        "https://sub.example.com/AbCdEf0123456789")]
    [InlineData("throne://subscribe?url=https%3A%2F%2Fsub.example.com%2Fabc%3Ftoken%3D1", "https://sub.example.com/abc?token=1")]
    [InlineData("throne://subscribe?url=https://sub.example.com/abc?a=1&b=2", "https://sub.example.com/abc?a=1&b=2")]
    [InlineData("sing-box://import-remote-profile?url=https%3A%2F%2Fsub.example.com%2Fabc#My%20VPN",
        "https://sub.example.com/abc#My VPN")]
    [InlineData("clash://install-config?name=x&url=https%3A%2F%2Fsub.example.com%2Fabc", "https://sub.example.com/abc")]
    [InlineData("hiddify://import/https://sub.example.com/abc", "https://sub.example.com/abc")]
    [InlineData("v2raytun://import/https://sub.example.com/abc", "https://sub.example.com/abc")]
    public void Normalize_AcceptsHttpAndImportLinks(string input, string expected)
    {
        Assert.Equal(SubscriptionLinkStatus.Ok, SubscriptionLink.Normalize(input, out var url));
        Assert.Equal(expected, url);
    }

    [Fact]
    public void Normalize_HappAddBase64()
    {
        var input = "happ://add/" + B64("https://sub.example.com/abc");
        Assert.Equal(SubscriptionLinkStatus.Ok, SubscriptionLink.Normalize(input, out var url));
        Assert.Equal("https://sub.example.com/abc", url);
    }

    [Theory]
    [InlineData("happ://crypt4/AbCdEf")]
    [InlineData("happ://crypt5/AbCdEf")]
    [InlineData("happ://crypt/AbCdEf")]
    public void Normalize_FlagsEncryptedHappLinks(string input)
    {
        Assert.Equal(SubscriptionLinkStatus.HappEncrypted, SubscriptionLink.Normalize(input, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com/sub")]
    [InlineData("happ://routing/add/eyJ9")]
    [InlineData("not a url")]
    [InlineData("throne://subscribe?name=x")]
    [InlineData("throne://subscribe?url=ftp://example.com/sub")]
    [InlineData("vless://uuid@host:443")]
    public void Normalize_RejectsOthers(string input)
    {
        Assert.Equal(SubscriptionLinkStatus.Invalid, SubscriptionLink.Normalize(input, out _));
    }

    [Theory]
    [InlineData("https://url.com/sub#Your_TITLE?installid=Abcd1234", "Your_TITLE")]
    [InlineData("https://url.com/sub#My%20VPN", "My VPN")]
    [InlineData("https://url.com/sub", null)]
    [InlineData("https://url.com/sub#", null)]
    public void FragmentTitle_ReadsHappFragment(string url, string? expected)
    {
        Assert.Equal(expected, SubscriptionLink.FragmentTitle(url));
    }
}
