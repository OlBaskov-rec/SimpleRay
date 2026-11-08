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
        store.Save(new RoutingSettings { Mode = RoutingMode.Global, BlockAds = false, KillSwitch = true });
        Assert.Equal(RoutingMode.Global, store.Load().Mode);
        Assert.False(store.Load().BlockAds);
        Assert.True(store.Load().KillSwitch);

        // Legacy: bare RoutingSettings object (no schemaVersion envelope) predating kill-switch.
        File.WriteAllText(AppPaths.SettingsFile, "{\"Mode\":\"Direct\",\"BlockAds\":true}");
        var legacy = new SettingsStore().Load();
        Assert.Equal(RoutingMode.Direct, legacy.Mode);
        Assert.True(legacy.BlockAds);
        Assert.False(legacy.KillSwitch); // absent in old files => off
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
        var updated = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        store.Save(new[]
        {
            new SubscriptionEntry
            {
                Url = "https://a/sub",
                UserAgent = "SomeClient/1.0",
                Name = "Домашняя",
                LastUpdated = updated,
                Info = new SubscriptionMetadata { Title = "Провайдер", Total = 100, Download = 40, UpdateIntervalHours = 6 },
            },
            new SubscriptionEntry { Url = "https://b/sub" },
            new SubscriptionEntry { Url = "https://a/sub" },
        });

        var loaded = store.Load();

        Assert.Equal(new[] { "https://a/sub", "https://b/sub" }, loaded.Select(e => e.Url).ToArray());
        Assert.Equal("Провайдер", loaded[0].Info.Title);
        Assert.Equal(100, loaded[0].Info.Total);
        Assert.Equal(40, loaded[0].Info.Used);
        Assert.Equal(6, loaded[0].Info.UpdateIntervalHours);
        Assert.Equal(updated, loaded[0].LastUpdated);
        Assert.Equal("SomeClient/1.0", loaded[0].UserAgent);
        Assert.Equal("Домашняя", loaded[0].Name);
        Assert.Null(loaded[1].Name);
        Assert.Null(loaded[1].LastUpdated);
        Assert.Null(loaded[1].UserAgent);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Client/1.2 (Android)  ", "Client/1.2 (Android)")]
    [InlineData("Bad\r\nX-Injected: 1", null)]
    public void SubscriptionService_NormalizesUserAgent(string? input, string? expected)
    {
        Assert.Equal(expected, SubscriptionService.NormalizeUserAgent(input));
    }

    [Fact]
    public void SubscriptionService_TruncatesLongUserAgent()
    {
        Assert.Equal(512, SubscriptionService.NormalizeUserAgent(new string('a', 600))!.Length);
    }

    [Fact]
    public void SubscriptionStore_EncryptsOnDisk()
    {
        // Subscription URLs carry access tokens: nothing of them may appear in cleartext.
        new SubscriptionStore().Save(new[]
        {
            new SubscriptionEntry { Url = "https://sub.example.com/SecretToken123", UserAgent = "Client/1.0" },
        });

        var raw = File.ReadAllText(AppPaths.SubscriptionsFile);
        Assert.DoesNotContain("SecretToken123", raw);
        Assert.DoesNotContain("sub.example.com", raw);
        Assert.Equal("https://sub.example.com/SecretToken123", new SubscriptionStore().Load().Single().Url);
    }

    [Fact]
    public void SubscriptionStore_ReadsLegacyPlaintext_AndEncryptsOnNextSave()
    {
        File.WriteAllText(AppPaths.SubscriptionsFile,
            "{\"SchemaVersion\":2,\"Items\":[{\"Url\":\"https://sub.example.com/PlainToken\"}]}");
        var store = new SubscriptionStore();

        var loaded = store.Load();
        Assert.Equal("https://sub.example.com/PlainToken", loaded.Single().Url);

        store.Save(loaded);
        Assert.DoesNotContain("PlainToken", File.ReadAllText(AppPaths.SubscriptionsFile));
        Assert.Equal("https://sub.example.com/PlainToken", store.Load().Single().Url);
    }

    [Fact]
    public void SubscriptionStore_ReadsSchema1UrlList()
    {
        // Files written before metadata existed hold a bare URL list.
        File.WriteAllText(AppPaths.SubscriptionsFile,
            "{\"SchemaVersion\":1,\"Urls\":[\"https://a/sub\",\"https://b/sub\"]}");

        var loaded = new SubscriptionStore().Load();

        Assert.Equal(new[] { "https://a/sub", "https://b/sub" }, loaded.Select(e => e.Url).ToArray());
        Assert.All(loaded, e =>
        {
            Assert.NotNull(e.Info);
            Assert.Null(e.LastUpdated); // never fetched by this build → due on launch
        });
    }
}
