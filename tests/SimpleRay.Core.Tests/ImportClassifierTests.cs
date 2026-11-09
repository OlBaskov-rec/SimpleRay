using System;
using System.Text;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class ImportClassifierTests
{
    private const string Trojan = "trojan://pw@b.example.com:8443#B";
    private const string Vless = "vless://uuid-a@a.example.com:443?security=reality&pbk=PBK&sid=ab&type=tcp#A";

    [Fact]
    public void Empty_IsEmpty()
    {
        Assert.True(ImportClassifier.Classify("").IsEmpty);
        Assert.True(ImportClassifier.Classify("  \n ").IsEmpty);
        Assert.True(ImportClassifier.Classify(null).IsEmpty);
    }

    [Fact]
    public void ServerLinks_GoToServers()
    {
        var plan = ImportClassifier.Classify(Vless + "\n" + Trojan);
        Assert.Empty(plan.SubscriptionUrls);
        Assert.Equal(Vless + "\n" + Trojan, plan.ServerText);
    }

    [Theory]
    [InlineData("https://sub.example.com/abc", "https://sub.example.com/abc")]
    [InlineData("happ://add/https://sub.example.com/abc", "https://sub.example.com/abc")]
    [InlineData("throne://subscribe?url=https%3A%2F%2Fsub.example.com%2Fabc", "https://sub.example.com/abc")]
    public void SubscriptionLinks_GoToSubscriptions(string input, string url)
    {
        var plan = ImportClassifier.Classify(input);
        Assert.Equal(new[] { url }, plan.SubscriptionUrls);
        Assert.Equal("", plan.ServerText);
    }

    [Fact]
    public void Mixed_IsSplit_AndUrlsDeduped()
    {
        var plan = ImportClassifier.Classify(
            "https://sub.example.com/abc\r\n" + Trojan + "\n\nhappp\nhttps://sub.example.com/abc\nhy2://pw@hy.example.com#H");
        Assert.Equal(new[] { "https://sub.example.com/abc" }, plan.SubscriptionUrls);
        Assert.Equal(Trojan + "\nhappp\nhy2://pw@hy.example.com#H", plan.ServerText);
        Assert.False(plan.HappEncrypted);
    }

    [Fact]
    public void HappCrypt_IsFlagged_NotTreatedAsServer()
    {
        var plan = ImportClassifier.Classify("happ://crypt5/AbCd\n" + Trojan);
        Assert.True(plan.HappEncrypted);
        Assert.Empty(plan.SubscriptionUrls);
        Assert.Equal(Trojan, plan.ServerText);
    }

    [Fact]
    public void Base64SubscriptionBody_GoesToServers_AndStillParses()
    {
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(Vless + "\n" + Trojan));
        var plan = ImportClassifier.Classify(body);
        Assert.Empty(plan.SubscriptionUrls);
        Assert.Equal(2, SubscriptionParser.Parse(plan.ServerText).Count);
    }

    [Fact]
    public void JsonAndWireGuard_AreKeptWhole()
    {
        const string json = "[ { \"remarks\": \"x\", \"outbounds\": [] } ]";
        Assert.Equal(json, ImportClassifier.Classify(json).ServerText);

        const string conf = "[Interface]\nPrivateKey = k\nAddress = 10.0.0.2/32\n\n[Peer]\nPublicKey = p\nEndpoint = wg.example.com:51820\n";
        var plan = ImportClassifier.Classify(conf);
        Assert.Empty(plan.SubscriptionUrls);
        Assert.Equal(conf, plan.ServerText);
    }
}
