using SimpleRay.App.Services;

namespace SimpleRay.App.Tests;

public class LanDnsDetectorTests
{
    private static LanDnsAdapter Adapter(string suffix = "hk.local", string server = "172.17.0.2") =>
        new("Ethernet", true, true, suffix, new[] { server });

    [Theory]
    [InlineData("hk.local", "hk.local")]
    [InlineData(".HK.LOCAL.", "hk.local")]
    [InlineData("office.lan", "office.lan")]
    [InlineData("home.arpa", "home.arpa")]
    [InlineData("office.home.arpa", "office.home.arpa")]
    [InlineData("corp", "corp")]
    [InlineData("office.home", "office.home")]
    [InlineData("office.corp", "office.corp")]
    [InlineData("office.internal", "office.internal")]
    [InlineData("office.intranet", "office.intranet")]
    [InlineData("office.localdomain", "office.localdomain")]
    [InlineData("office.private", "office.private")]
    public void InternalSuffix_IsAcceptedAndNormalized(string suffix, string expected)
    {
        var result = LanDnsDetector.Select(new[] { Adapter(suffix) });
        Assert.NotNull(result);
        Assert.Equal(new[] { expected }, result.Suffixes);
        Assert.Equal("172.17.0.2", result.Server);
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("com")]
    [InlineData("ru")]
    [InlineData("office")]
    [InlineData("evilhome.arpa")]
    [InlineData("home.arpa.evil.com")]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("office..local")]
    [InlineData("office_local")]
    [InlineData("office/lan")]
    [InlineData("office local")]
    [InlineData("\u043e\u0444\u0438\u0441.local")]
    [InlineData("-office.local")]
    public void UnsafeSuffix_IsRejected(string suffix) =>
        Assert.Null(LanDnsDetector.Select(new[] { Adapter(suffix) }));

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.2", false)]
    [InlineData("172.15.255.255", false)]
    [InlineData("172.32.0.0", false)]
    [InlineData("192.169.0.1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:172.17.0.2", false)]
    [InlineData("invalid", false)]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.0.1", true)]
    public void OnlyRfc1918Ipv4_IsAccepted(string server, bool accepted) =>
        Assert.Equal(accepted, LanDnsDetector.Select(new[] { Adapter(server: server) }) is not null);

    [Fact]
    public void FirstPrivateDns_IsChosenInOrder()
    {
        var result = LanDnsDetector.Select(new[] { Adapter() with
        {
            DnsServers = new[] { "8.8.8.8", "::1", "172.17.0.2", "172.17.0.3" },
        } });
        Assert.Equal("172.17.0.2", result!.Server);
    }

    [Fact]
    public void UnusableAdapters_AreSkipped_ThenFirstEligibleIsChosen()
    {
        var good = Adapter();
        var rejected = new[]
        {
            good with { Name = "SiMpLeRaY" }, good with { IsLoopback = true },
            good with { IsUp = false }, good with { HasDefaultGateway = false },
            Adapter("google.com"), Adapter(server: "8.8.8.8"),
        };
        foreach (var adapter in rejected)
        {
            Assert.Null(LanDnsDetector.Select(new[] { adapter }));
            var result = LanDnsDetector.Select(new[] { adapter, good, Adapter("other.lan") });
            Assert.Equal("hk.local", Assert.Single(result!.Suffixes));
        }
        Assert.Null(LanDnsDetector.Select(Array.Empty<LanDnsAdapter>()));
        Assert.Null(LanDnsDetector.Select(rejected));
        Assert.Null(LanDnsDetector.Select(new[] { good with { Name = "custom-tun" } }, "custom-tun"));
    }
}
