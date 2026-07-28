using SimpleRay.Core.Config;
using SimpleRay.Core.Dns;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class DnsSettingsTests
{
    private static ProfileConfig Vless() =>
        ShareLinkParser.Parse("vless://uuid-x@example.com:443?security=tls#node");

    private static string ServerOfTag(RoutingSettings routing, string tag) =>
        (string)SingBoxConfigGenerator.Generate(Vless(), routing)["dns"]!["servers"]!
            .AsArray().First(s => (string?)s!["tag"] == tag)!["server"]!;

    [Fact]
    public void ChosenProviders_LandInTheGeneratedConfig()
    {
        var routing = new RoutingSettings
        {
            Dns = new DnsSettings { LocalProviderId = "adguard", RemoteProviderId = "google" },
        };

        Assert.Equal("94.140.14.14", ServerOfTag(routing, "local"));
        Assert.Equal("8.8.8.8", ServerOfTag(routing, "remote"));
    }

    [Fact]
    public void UnknownProviderId_FallsBackInsteadOfEmittingGarbage()
    {
        // A settings file from a newer build (or hand-edited) must not produce a config
        // with an empty DNS server, which sing-box would reject at startup.
        var routing = new RoutingSettings
        {
            Dns = new DnsSettings { LocalProviderId = "no-such-provider", RemoteProviderId = "" },
        };

        Assert.Equal("1.1.1.1", ServerOfTag(routing, "local"));
        Assert.Equal("1.1.1.1", ServerOfTag(routing, "remote"));
    }

    [Fact]
    public void DefaultSettings_UseCloudflareNotAliDns()
    {
        // Regression guard: the original hardcoded local resolver was AliDNS (223.5.5.5),
        // which is both the slowest reachable option and an odd privacy default.
        var routing = new RoutingSettings();

        Assert.Equal("1.1.1.1", ServerOfTag(routing, "local"));
        Assert.NotEqual("223.5.5.5", ServerOfTag(routing, "local"));
    }

    [Fact]
    public void LocalAndRemote_KeepTheirDetours()
    {
        var dns = SingBoxConfigGenerator.Generate(Vless(), new RoutingSettings())["dns"]!["servers"]!.AsArray();

        // "local" must bypass the tunnel and "remote" must go through it, whatever
        // provider is selected — otherwise direct traffic resolves through the proxy.
        Assert.Equal("direct", (string?)dns.First(s => (string?)s!["tag"] == "local")!["detour"]);
        Assert.Equal("proxy", (string?)dns.First(s => (string?)s!["tag"] == "remote")!["detour"]);
    }

    [Fact]
    public void EveryCatalogEntry_IsAnIpLiteral_NotAHostname()
    {
        // The resolver resolves everything else, so a hostname would need its own
        // bootstrap resolver. See DnsProvider.
        Assert.All(DnsCatalog.All, p => Assert.True(System.Net.IPAddress.TryParse(p.Server, out _),
            $"{p.Id} must be addressed by IP, got '{p.Server}'"));
    }

    [Fact]
    public void CatalogIds_AreUnique()
    {
        Assert.Equal(DnsCatalog.All.Count, DnsCatalog.All.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("zh", "alidns")]
    [InlineData("zh-CN", "alidns")]
    [InlineData("ru", "cloudflare")]
    [InlineData("en", "cloudflare")]
    [InlineData(null, "cloudflare")]
    public void InitialIdForLanguage_PicksAKnownProvider(string? language, string expected)
    {
        var id = DnsCatalog.InitialIdForLanguage(language);

        Assert.Equal(expected, id);
        Assert.NotNull(DnsCatalog.Find(id));
    }
}
