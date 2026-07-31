using System;
using System.IO;
using System.Threading.Tasks;
using SimpleRay.App.Engine;
using SimpleRay.Core.Config;
using SimpleRay.Core.Engine;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;

namespace SimpleRay.App.Tests;

/// <summary>
/// Config-validation behaviour of SingBoxEngine. The cases that need the real binary
/// locate sing-box.exe under the App build output (present after fetch-deps / in CI) and
/// soft-skip when it is absent, so a dev without the deps still gets a green suite.
/// </summary>
public class EngineValidationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "srayeng_" + Guid.NewGuid().ToString("N"));

    public EngineValidationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private SingBoxEngine Engine(string exePath) => new(new EngineOptions
    {
        ExecutablePath = exePath,
        WorkingDirectory = _dir,
    });

    /// <summary>A config with no external rule-set files, so `check` needs only the binary.</summary>
    private static string ValidConfig(string workDir)
    {
        var profile = ShareLinkParser.Parse("vless://11111111-1111-1111-1111-111111111111@example.com:443?security=tls#n");
        var routing = new RoutingSettings { Mode = RoutingMode.Global, BlockAds = false };
        return SingBoxConfigGenerator.GenerateJson(
            new[] { profile }, routing, new GeneratorOptions { RuleSetDirectory = workDir });
    }

    [Fact]
    public async Task StartAsync_MissingExecutable_Throws()
    {
        var engine = Engine(Path.Combine(_dir, "does-not-exist.exe"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => engine.StartAsync(ValidConfig(_dir)));
        Assert.NotEqual(EngineState.Running, engine.State);
    }

    [Fact]
    public async Task CheckConfig_RejectsBrokenConfig()
    {
        var exe = FindSingBox();
        if (exe is null) return; // deps not fetched — skip

        var (ok, output) = await Engine(exe).CheckConfigAsync("{ this is not valid json");
        Assert.False(ok);
        Assert.NotEmpty(output);
    }

    [Fact]
    public async Task CheckConfig_AcceptsGeneratedConfig()
    {
        var exe = FindSingBox();
        if (exe is null) return; // deps not fetched — skip

        var (ok, output) = await Engine(exe).CheckConfigAsync(ValidConfig(_dir));
        Assert.True(ok, "generated config should pass sing-box check; output: " + output);
    }

    [Fact]
    public async Task StartAsync_InvalidConfig_ThrowsBeforeStarting()
    {
        var exe = FindSingBox();
        if (exe is null) return; // deps not fetched — skip

        var engine = Engine(exe);
        // Validation runs before the tunnel is spawned, so this throws without needing
        // admin/TUN and the engine never reaches Running.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.StartAsync("{ broken"));
        Assert.Contains("Invalid configuration", ex.Message);
        Assert.NotEqual(EngineState.Running, engine.State);
    }

    /// <summary>Walks up to the repo root and finds a built sing-box.exe, or null.</summary>
    private static string? FindSingBox()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SimpleRay.sln")))
            dir = dir.Parent;
        if (dir is null) return null;

        foreach (var path in new[]
        {
            Path.Combine(dir.FullName, "runtime", "core", "sing-box.exe"),
        })
            if (File.Exists(path)) return path;

        var appBin = Path.Combine(dir.FullName, "src", "SimpleRay.App", "bin");
        if (Directory.Exists(appBin))
        {
            var hit = Directory.GetFiles(appBin, "sing-box.exe", SearchOption.AllDirectories);
            if (hit.Length > 0) return hit[0];
        }
        return null;
    }
}
