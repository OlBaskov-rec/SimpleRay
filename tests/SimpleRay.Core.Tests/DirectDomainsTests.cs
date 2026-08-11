using System.Linq;
using System.Text.Json.Nodes;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class DirectDomainsTests
{
    private static ProfileConfig Vless() =>
        ShareLinkParser.Parse("vless://uuid-x@example.com:443?security=tls#node");

    private static JsonArray Rules(RoutingSettings r) =>
        SingBoxConfigGenerator.Generate(Vless(), r)["route"]!["rules"]!.AsArray();

    [Fact]
    public void DirectDomains_EmitDomainSuffixDirectRule()
    {
        var routing = new RoutingSettings
        {
            Mode = RoutingMode.Rule,
            DirectDomains = new() { "yandex.ru", "mail.ru" },
        };

        var rule = Rules(routing).FirstOrDefault(r => r!["domain_suffix"] is JsonArray);
        Assert.NotNull(rule);
        Assert.Equal("direct", (string?)rule!["outbound"]);
        var suffixes = ((JsonArray)rule["domain_suffix"]!).Select(x => (string?)x).ToList();
        Assert.Contains("yandex.ru", suffixes);
        Assert.Contains("mail.ru", suffixes);
    }

    [Fact]
    public void DirectDomains_AreNormalised()
    {
        var routing = new RoutingSettings
        {
            Mode = RoutingMode.Rule,
            DirectDomains = new() { " HTTPS://Mail.Yandex.RU/inbox ", ".disk.yandex.net", "yandex.ru", "yandex.ru", "" },
        };

        var rule = Rules(routing).First(r => r!["domain_suffix"] is JsonArray);
        var suffixes = ((JsonArray)rule!["domain_suffix"]!).Select(x => (string?)x).ToList();

        Assert.Contains("mail.yandex.ru", suffixes); // scheme + path stripped, lowercased
        Assert.Contains("disk.yandex.net", suffixes); // leading dot stripped
        Assert.Equal(suffixes.Count, suffixes.Distinct().Count()); // deduped
        Assert.DoesNotContain("", suffixes);
    }

    [Fact]
    public void DirectDomains_AlsoResolveViaLocalDns()
    {
        var routing = new RoutingSettings
        {
            Mode = RoutingMode.Rule,
            DirectDomains = new() { "yandex.ru" },
        };

        var dnsRules = SingBoxConfigGenerator.Generate(Vless(), routing)["dns"]!["rules"]!.AsArray();
        var rule = dnsRules.FirstOrDefault(r => r!["domain_suffix"] is JsonArray);
        Assert.NotNull(rule);
        Assert.Equal("local", (string?)rule!["server"]); // direct resolver, not the tunnelled one
        var suffixes = ((JsonArray)rule["domain_suffix"]!).Select(x => (string?)x).ToList();
        Assert.Contains("yandex.ru", suffixes);
    }

    [Fact]
    public void NoDirectDomains_NoDnsRule()
    {
        var routing = new RoutingSettings { Mode = RoutingMode.Rule };
        var dns = SingBoxConfigGenerator.Generate(Vless(), routing)["dns"]!.AsObject();
        Assert.False(dns.ContainsKey("rules")); // no direct domains -> no DNS override
    }

    [Fact]
    public void DirectDomains_OnlyInRuleMode()
    {
        var routing = new RoutingSettings { Mode = RoutingMode.Global, DirectDomains = new() { "yandex.ru" } };
        Assert.DoesNotContain(Rules(routing), r => r!["domain_suffix"] is JsonArray);
    }

    [Fact]
    public void NoDirectDomains_NoRule()
    {
        var routing = new RoutingSettings { Mode = RoutingMode.Rule };
        Assert.DoesNotContain(Rules(routing), r => r!["domain_suffix"] is JsonArray);
    }
}
