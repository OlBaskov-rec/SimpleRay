using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleRay.App.Engine;

namespace SimpleRay.App.Tests;

public class TunnelLivenessMonitorTests
{
    // Delay stubbed to complete immediately; the monitor is stopped from onDead so the loop ends.
    private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay = (_, _) => Task.CompletedTask;

    [Fact]
    public async Task FiresOnDead_AfterThresholdConsecutiveFailures()
    {
        int probes = 0, deaths = 0;
        TunnelLivenessMonitor mon = null!;
        mon = new TunnelLivenessMonitor(
            probe: _ => { probes++; return Task.FromResult(false); },     // tunnel always dead
            onDead: _ => { deaths++; mon.Stop(); return Task.CompletedTask; },
            interval: TimeSpan.Zero, threshold: 3, delay: NoDelay);

        mon.Start();
        try { await mon.LoopTask!; } catch { }

        Assert.Equal(1, deaths);
        Assert.Equal(3, probes); // fires on exactly the 3rd failure, not before
    }

    [Fact]
    public async Task SuccessResetsTheFailureCount()
    {
        // F, F, T (reset), F, F, F -> death only on the 6th probe.
        var results = new Queue<bool>(new[] { false, false, true, false, false, false });
        int probes = 0, deaths = 0;
        TunnelLivenessMonitor mon = null!;
        mon = new TunnelLivenessMonitor(
            probe: _ => { probes++; return Task.FromResult(results.Count > 0 && results.Dequeue()); },
            onDead: _ => { deaths++; mon.Stop(); return Task.CompletedTask; },
            interval: TimeSpan.Zero, threshold: 3, delay: NoDelay);

        mon.Start();
        try { await mon.LoopTask!; } catch { }

        Assert.Equal(1, deaths);
        Assert.Equal(6, probes);
    }

    [Fact]
    public async Task Stop_EndsTheLoopWithoutFiring()
    {
        int deaths = 0;
        var gate = new TaskCompletionSource();
        var mon = new TunnelLivenessMonitor(
            probe: _ => Task.FromResult(true),                 // tunnel healthy
            onDead: _ => { deaths++; return Task.CompletedTask; },
            interval: TimeSpan.Zero, threshold: 3,
            delay: async (_, ct) => { await gate.Task.WaitAsync(ct).ConfigureAwait(false); });

        mon.Start();
        mon.Stop();          // cancel while parked in delay
        gate.TrySetResult(); // release (already cancelled)
        try { await mon.LoopTask!; } catch { }

        Assert.Equal(0, deaths);
    }
}
