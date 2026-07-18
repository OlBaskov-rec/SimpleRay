using System;
using System.Text;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class SubscriptionParserTests
{
    private const string TwoLinks =
        "vless://uuid-a@a.example.com:443?security=reality&pbk=PBK&sid=ab&type=tcp#A\n" +
        "trojan://pw@b.example.com:8443#B";

    [Fact]
    public void Base64Body_DecodesToProfiles()
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(TwoLinks));
        var list = SubscriptionParser.Parse(b64);

        Assert.Equal(2, list.Count);
        Assert.Equal(ProxyProtocol.VLESS, list[0].Protocol);
        Assert.Equal("a.example.com", list[0].Server);
        Assert.Equal(ProxyProtocol.Trojan, list[1].Protocol);
        Assert.Equal("b.example.com", list[1].Server);
    }

    [Fact]
    public void PlainTextBody_ParsesDirectly()
    {
        var list = SubscriptionParser.Parse(TwoLinks);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void UrlSafeBase64WithoutPadding_Works()
    {
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(TwoLinks))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var list = SubscriptionParser.Parse(raw);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void GarbageBody_ReturnsEmpty()
    {
        Assert.Empty(SubscriptionParser.Parse("not a subscription at all"));
        Assert.Empty(SubscriptionParser.Parse(""));
    }
}
