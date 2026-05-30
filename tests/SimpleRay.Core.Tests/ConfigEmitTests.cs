using System.IO;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

/// <summary>
/// Dev helper: emits a real config.json to a path so it can be validated with
/// `sing-box check`. No-op unless SR_EMIT_CONFIG is set, so normal runs ignore it.
/// </summary>
public class ConfigEmitTests
{
    [Fact]
    public void EmitRealConfig()
    {
        var outPath = Environment.GetEnvironmentVariable("SR_EMIT_CONFIG");
        if (string.IsNullOrEmpty(outPath))
            return;

        var geoDir = Environment.GetEnvironmentVariable("SR_GEO_DIR") ?? "geo";

        var profile = ShareLinkParser.Parse(
            "vless://b831381d-6324-4d53-ad4f-8cda48b30811@example.com:443" +
            "?security=reality&sni=www.microsoft.com&fp=chrome" +
            "&pbk=66dK2tcRJ1R6fc4cbukmnRBZPZh6tLMcRR58KCLt6AU&sid=ab12" +
            "&type=tcp&flow=xtls-rprx-vision#test");

        var routing = new RoutingSettings
        {
            Mode = RoutingMode.Rule,
            DirectGeosite = new() { "geosite-private", "geosite-category-ru" },
            DirectGeoip = new() { "geoip-ru" },
            BlockAds = true,
            ProxyProcesses = new() { "chrome" },
        };

        var json = SingBoxConfigGenerator.GenerateJson(
            profile, routing, new GeneratorOptions { RuleSetDirectory = geoDir });
        File.WriteAllText(outPath, json);
    }

    /// <summary>Emits a 2-server urltest failover config for `sing-box check`. Gated on SR_EMIT_GROUP.</summary>
    [Fact]
    public void EmitGroupConfig()
    {
        var outPath = Environment.GetEnvironmentVariable("SR_EMIT_GROUP");
        if (string.IsNullOrEmpty(outPath))
            return;

        var geoDir = Environment.GetEnvironmentVariable("SR_GEO_DIR") ?? "geo";

        ProfileConfig P(string server) => ShareLinkParser.Parse(
            $"vless://b831381d-6324-4d53-ad4f-8cda48b30811@{server}:443" +
            "?security=reality&sni=www.microsoft.com&fp=chrome" +
            "&pbk=66dK2tcRJ1R6fc4cbukmnRBZPZh6tLMcRR58KCLt6AU&sid=ab12&type=tcp#n");

        var profiles = new[] { P("cheap.example.com"), P("fancy.example.com") };
        var routing = new RoutingSettings
        {
            Mode = RoutingMode.Rule,
            DirectGeosite = new() { "geosite-private", "geosite-category-ru" },
            DirectGeoip = new() { "geoip-ru" },
            BlockAds = true,
            Failover = new GroupSettings { Mode = FailoverMode.UrlTest },
        };

        var json = SingBoxConfigGenerator.GenerateJson(
            profiles, routing, new GeneratorOptions { RuleSetDirectory = geoDir });
        File.WriteAllText(outPath, json);
    }

    /// <summary>Emits a WireGuard config for `sing-box check`. Gated on SR_EMIT_WG.</summary>
    [Fact]
    public void EmitWireGuardConfig()
    {
        var outPath = Environment.GetEnvironmentVariable("SR_EMIT_WG");
        if (string.IsNullOrEmpty(outPath))
            return;

        var geoDir = Environment.GetEnvironmentVariable("SR_GEO_DIR") ?? "geo";

        var profile = WireGuardConfParser.Parse(
            "[Interface]\n" +
            "PrivateKey = SC7/JBU2Flu24hIOfr7GTnyciTSnFzBKTJ2+K68wB2w=\n" +
            "Address = 10.66.66.2/32, fd42::2/128\n" +
            "MTU = 1420\n\n" +
            "[Peer]\n" +
            "PublicKey = ERKwnts0P6QNOLZqJUQM7g723+bp7sNgjbeUm44pEFI=\n" +
            "AllowedIPs = 0.0.0.0/0, ::/0\n" +
            "Endpoint = wg.example.com:51820\n" +
            "PersistentKeepalive = 25\n");

        var routing = new RoutingSettings { Mode = RoutingMode.Rule, BlockAds = true };
        var json = SingBoxConfigGenerator.GenerateJson(
            profile, routing, new GeneratorOptions { RuleSetDirectory = geoDir });
        File.WriteAllText(outPath, json);
    }
}
