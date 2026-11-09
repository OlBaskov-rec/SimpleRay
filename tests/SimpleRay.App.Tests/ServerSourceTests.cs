using System.Collections.Generic;
using System.Linq;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Services;
using SimpleRay.App.ViewModels;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Tests;

public class ServerSourceTests
{
    private static ProfileConfig Server(string tag, string? subscription = null) =>
        new() { Tag = tag, Protocol = ProxyProtocol.Trojan, Server = tag + ".example.com", Port = 443, Subscription = subscription };

    private const string UrlA = "https://a.example.com/sub";
    private const string UrlB = "https://b.example.com/sub";

    [Fact]
    public void Build_ListsAll_Manual_ThenEachSubscription()
    {
        var subs = new List<SubscriptionEntry>
        {
            new() { Url = UrlA, Info = new SubscriptionMetadata { Title = "Provider A" } },
            new() { Url = UrlB, Name = "Mine" },
        };
        var profiles = new List<ProfileConfig> { Server("m1"), Server("a1", UrlA), Server("a2", UrlA) };

        var sources = ServerSource.Build(subs, profiles);

        Assert.Equal(new[] { ServerSourceKind.All, ServerSourceKind.Manual, ServerSourceKind.Subscription, ServerSourceKind.Subscription },
            sources.Select(s => s.Kind));
        Assert.Equal("Provider A", sources[2].Title);
        Assert.Equal("Mine", sources[3].Title);
        Assert.Equal("sub:" + UrlA, sources[2].Key);
        Assert.Contains("2", sources[2].Details); // its server count
    }

    [Fact]
    public void Build_HidesManualRow_WhenEveryServerComesFromASubscription()
    {
        var subs = new List<SubscriptionEntry> { new() { Url = UrlA } };
        var sources = ServerSource.Build(subs, new List<ProfileConfig> { Server("a1", UrlA) });
        Assert.DoesNotContain(sources, s => s.Kind == ServerSourceKind.Manual);
    }

    [Fact]
    public void Build_KeepsManualRow_WhenThereAreNoSubscriptions()
    {
        var sources = ServerSource.Build(new List<SubscriptionEntry>(), new List<ProfileConfig>());
        Assert.Equal(new[] { ServerSourceKind.All, ServerSourceKind.Manual }, sources.Select(s => s.Kind));
    }

    [Fact]
    public void Matches_FiltersBySource()
    {
        var subs = new List<SubscriptionEntry> { new() { Url = UrlA }, new() { Url = UrlB } };
        var manual = Server("m1");
        var fromA = Server("a1", UrlA);
        var fromB = Server("b1", UrlB);
        var sources = ServerSource.Build(subs, new List<ProfileConfig> { manual, fromA, fromB });

        var all = sources.Single(s => s.Kind == ServerSourceKind.All);
        var man = sources.Single(s => s.Kind == ServerSourceKind.Manual);
        var a = sources.Single(s => s.Entry?.Url == UrlA);

        Assert.True(all.Matches(manual) && all.Matches(fromA) && all.Matches(fromB));
        Assert.True(man.Matches(manual));
        Assert.False(man.Matches(fromA));
        Assert.True(a.Matches(fromA));
        Assert.False(a.Matches(fromB));
        Assert.False(a.Matches(manual));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    public void UrlLauncher_RefusesNonHttp(string? url)
    {
        Assert.False(UrlLauncher.TryOpen(url));
    }
}
