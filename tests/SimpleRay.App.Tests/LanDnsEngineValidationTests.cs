using System.IO;
using SimpleRay.App.Engine;
using SimpleRay.Core.Config;
using SimpleRay.Core.Dns;
using SimpleRay.Core.Engine;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;

namespace SimpleRay.App.Tests;

public class LanDnsEngineValidationTests
{
    [Theory]
    [InlineData(RoutingMode.Global)]
    [InlineData(RoutingMode.Rule)]
    [InlineData(RoutingMode.Direct)]
    public async Task GeneratedConfig_WithLanDns_PassesRealSingBoxCheck(RoutingMode mode)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SimpleRay.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var appBin = Path.Combine(root.FullName, "src", "SimpleRay.App", "bin");
        var exe = Directory.GetFiles(appBin, "sing-box.exe", SearchOption.AllDirectories).FirstOrDefault();
        // This test must actually validate the schema, rather than silently pass without deps.
        Assert.True(exe is not null, "Fetch runtime dependencies before running real sing-box validation.");
        var dir = Path.Combine(Path.GetTempPath(), "sray_lan_dns_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var profile = ShareLinkParser.Parse(
                "vless://11111111-1111-1111-1111-111111111111@example.com:443?security=tls#n");
            var routing = new RoutingSettings
            {
                Mode = mode, BlockAds = false, DirectGeosite = new(), DirectGeoip = new(),
                DirectDomains = new() { "example.org" }, ProxyDomains = new() { "hk.local" },
            };
            var config = SingBoxConfigGenerator.GenerateJson(profile, routing, new GeneratorOptions
            {
                RuleSetDirectory = dir,
                LanDns = new LanDns(new[] { "hk.local" }, "172.17.0.2"),
            });
            var engine = new SingBoxEngine(new EngineOptions
            {
                ExecutablePath = exe!, WorkingDirectory = dir,
            });
            var (ok, output) = await engine.CheckConfigAsync(config);
            Assert.True(ok, "LAN DNS config should pass sing-box check; output: " + output);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
