using SimpleRay.App.Engine;

namespace SimpleRay.App.Tests;

public class WfpCleanupTests
{
    [Fact]
    public void DeleteOurObjects_AllNotFound_ReturnsNoErrors()
    {
        int filters = 0;
        int sublayers = 0;
        int providers = 0;

        var errors = WfpKillSwitch.DeleteOurObjects(
            (_, _) => { filters++; return 0x80320003; },
            () => { sublayers++; return 0x80320007; },
            () => { providers++; return 0x80320005; });

        Assert.Empty(errors);
        Assert.Equal(26, filters);
        Assert.Equal(1, sublayers);
        Assert.Equal(1, providers);
    }

    [Fact]
    public void DeleteOurObjects_BlockAllFails_ReportsErrorAndAttemptsEveryDeletion()
    {
        var calls = new List<string>();
        var errors = WfpKillSwitch.DeleteOurObjects(
            (layer, tag) =>
            {
                calls.Add($"{layer}:{tag}");
                return layer == Native.FWPM_LAYER_ALE_AUTH_CONNECT_V4 && tag == "block-all"
                    ? 0x80320001u : 0u;
            },
            () => { calls.Add("sublayer"); return 0; },
            () => { calls.Add("provider"); return 0; });

        var error = Assert.Single(errors);
        Assert.Contains("block-all", error);
        Assert.Contains("v4", error);
        Assert.Contains("0x80320001", error);
        Assert.Equal(28, calls.Count);
        Assert.Contains($"{Native.FWPM_LAYER_ALE_AUTH_CONNECT_V6}:block-all", calls);
        Assert.Equal(new[] { "sublayer", "provider" }, calls.TakeLast(2));
    }

    [Fact]
    public void DisengageCore_FailureKeepsEngaged_ThenSuccessfulRetryClearsState()
    {
        var killSwitch = new WfpKillSwitch();
        int attempts = 0;
        int closes = 0;
        IReadOnlyList<string> Cleanup()
        {
            attempts++;
            return attempts == 1 ? new[] { "filter block-all (v4): 0x80320001" } : Array.Empty<string>();
        }

        var error = Assert.Throws<InvalidOperationException>(
            () => killSwitch.DisengageCore(Cleanup, () => closes++));

        Assert.StartsWith("Kill-switch filters could not be removed: ", error.Message);
        Assert.Contains("block-all", error.Message);
        Assert.Contains("0x80320001", error.Message);
        Assert.True(killSwitch.IsEngaged);
        Assert.Equal(0, closes);

        killSwitch.DisengageCore(Cleanup, () => closes++);

        Assert.False(killSwitch.IsEngaged);
        Assert.Equal(2, attempts);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void SkipEngage_AfterFailedDisengage_CleansUpFirstInsteadOfSkipping()
    {
        var killSwitch = new WfpKillSwitch();
        Assert.Throws<InvalidOperationException>(() => killSwitch.DisengageCore(
            () => new[] { "filter block-all (v4): 0x80320001" }, () => { }));
        Assert.True(killSwitch.IsEngaged);

        int cleanups = 0;
        bool skip = killSwitch.SkipEngage(() => { cleanups++; return Array.Empty<string>(); }, () => { });

        Assert.False(skip); // the stale filters were removed, so new ones must be installed
        Assert.Equal(1, cleanups);
        Assert.False(killSwitch.IsEngaged);
    }

    [Fact]
    public void SkipEngage_LeftoversStillStuck_Throws()
    {
        var killSwitch = new WfpKillSwitch();
        IReadOnlyList<string> Stuck() => new[] { "filter block-all (v4): 0x80320001" };
        Assert.Throws<InvalidOperationException>(() => killSwitch.DisengageCore(Stuck, () => { }));

        Assert.Throws<InvalidOperationException>(() => killSwitch.SkipEngage(Stuck, () => { }));
        Assert.True(killSwitch.IsEngaged);
    }
}
