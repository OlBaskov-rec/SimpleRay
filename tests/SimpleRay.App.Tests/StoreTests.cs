using System;
using System.IO;
using System.Linq;
using System.Text;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Services;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Tests;

/// <summary>Stores are redirected to a temp dir via AppPaths.DataDirOverride (serial — see TestSetup).</summary>
public class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sraytest_" + Guid.NewGuid().ToString("N"));

    public StoreTests()
    {
        Directory.CreateDirectory(_dir);
        AppPaths.DataDirOverride = _dir;
    }

    public void Dispose()
    {
        AppPaths.DataDirOverride = null;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void ProfileStore_RoundTrips_AndEncryptsOnDisk()
    {
        var store = new ProfileStore();
        var profiles = new List<ProfileConfig>
        {
            new() { Tag = "A", Protocol = ProxyProtocol.VLESS, Server = "a.com", Port = 443, Uuid = "u", Raw = "vless://a", InGroup = true },
            new() { Tag = "B", Protocol = ProxyProtocol.Trojan, Server = "b.com", Port = 8443, Password = "pw", Raw = "trojan://b" },
        };
        store.Save(profiles);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("a.com", loaded[0].Server);
        Assert.True(loaded[0].InGroup);
        Assert.Equal("pw", loaded[1].Password);

        // File is DPAPI-encrypted: the password must not appear in cleartext on disk.
        var raw = File.ReadAllText(AppPaths.ProfilesFile);
        Assert.DoesNotContain("pw", raw);
    }

    [Fact]
    public void ProfileStore_ReadsLegacyPlainArray()
    {
        // Old format: bare JSON array, unencrypted.
        File.WriteAllText(AppPaths.ProfilesFile,
            "[{\"Tag\":\"Legacy\",\"Protocol\":\"VLESS\",\"Server\":\"leg.com\",\"Port\":443,\"Raw\":\"vless://leg\"}]");
        var loaded = new ProfileStore().Load();
        Assert.Single(loaded);
        Assert.Equal("leg.com", loaded[0].Server);
    }

    [Fact]
    public void ProfileStore_CorruptFile_ReturnsEmpty()
    {
        File.WriteAllText(AppPaths.ProfilesFile, "}{ not json");
        Assert.Empty(new ProfileStore().Load());
    }

    [Fact]
    public void SettingsStore_RoundTrips_AndReadsLegacyBareObject()
    {
        var store = new SettingsStore();
        store.Save(new RoutingSettings { Mode = RoutingMode.Global, BlockAds = false });
        Assert.Equal(RoutingMode.Global, store.Load().Mode);
        Assert.False(store.Load().BlockAds);

        // Legacy: bare RoutingSettings object (no schemaVersion envelope).
        File.WriteAllText(AppPaths.SettingsFile, "{\"Mode\":\"Direct\",\"BlockAds\":true}");
        var legacy = new SettingsStore().Load();
        Assert.Equal(RoutingMode.Direct, legacy.Mode);
        Assert.True(legacy.BlockAds);
    }

    [Fact]
    public void SettingsStore_RoundTripsDnsChoice()
    {
        var store = new SettingsStore();
        store.Save(new RoutingSettings
        {
            Dns = new DnsSettings { LocalProviderId = "adguard", RemoteProviderId = "google" },
        });

        var loaded = store.Load();
        Assert.Equal("adguard", loaded.Dns.LocalProviderId);
        Assert.Equal("google", loaded.Dns.RemoteProviderId);
    }

    [Fact]
    public void SettingsStore_SettingsWrittenBeforeDnsExisted_LoadUsable()
    {
        // Files written by an older build have no "Dns" section at all; loading one
        // must yield usable defaults rather than a null that breaks config generation.
        File.WriteAllText(AppPaths.SettingsFile, "{\"Mode\":\"Rule\",\"BlockAds\":true}");

        var loaded = new SettingsStore().Load();

        Assert.NotNull(loaded.Dns);
        Assert.Null(loaded.Dns.LocalProviderId); // "never chosen" — the app seeds it by language
        Assert.NotNull(loaded.Dns.RemoteProviderId);
    }

    [Fact]
    public void SubscriptionStore_RoundTrips_AndDedups()
    {
        var store = new SubscriptionStore();
        store.Save(new[] { "https://a/sub", "https://b/sub", "https://a/sub" });
        var urls = store.Load();
        Assert.Equal(new[] { "https://a/sub", "https://b/sub" }, urls.ToArray());
    }
}
