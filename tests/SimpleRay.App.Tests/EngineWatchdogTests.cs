using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleRay.App.Engine;
using SimpleRay.Core.Engine;

namespace SimpleRay.App.Tests;

public class EngineWatchdogTests
{
    /// <summary>Minimal IVpnEngine whose StartAsync outcome is scripted per call.</summary>
    private sealed class FakeEngine : IVpnEngine
    {
        private readonly Func<int, bool> _startSucceeds;
        public int StartCalls { get; private set; }

        public FakeEngine(Func<int, bool> startSucceeds) => _startSucceeds = startSucceeds;

        public EngineState State { get; private set; } = EngineState.Stopped;
        public event EventHandler<EngineState>? StateChanged;
        public event EventHandler<string>? LogReceived;

        public Task StartAsync(string configJson, CancellationToken ct = default)
        {
            StartCalls++;
            SetState(EngineState.Starting);
            if (_startSucceeds(StartCalls))
            {
                SetState(EngineState.Running);
                return Task.CompletedTask;
            }
            SetState(EngineState.Faulted);
            throw new InvalidOperationException("start failed");
        }

        public Task StopAsync() { SetState(EngineState.Stopped); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => default;

        /// <summary>Simulate sing-box crashing on its own.</summary>
        public void Crash() => SetState(EngineState.Faulted);

        private void SetState(EngineState s) { State = s; StateChanged?.Invoke(this, s); }
        public void RaiseLog(string s) => LogReceived?.Invoke(this, s);
    }

    private static WatchdogOptions Instant(int maxAttempts = 5) => new()
    {
        MaxAttempts = maxAttempts,
        Backoff = _ => TimeSpan.Zero,
        Delay = (_, _) => Task.CompletedTask, // no real waiting in tests
    };

    [Fact]
    public async Task Crash_WhileArmed_ReconnectsOnFirstAttempt()
    {
        var engine = new FakeEngine(_ => true); // every start succeeds
        var wd = new EngineWatchdog(engine, Instant());
        int reconnectingFired = 0;
        wd.Reconnecting += (_, _) => reconnectingFired++;

        await engine.StartAsync("cfg");   // initial connect
        wd.Arm("cfg");
        engine.Crash();
        await wd.ReconnectTask!;

        Assert.Equal(2, engine.StartCalls); // initial + one reconnect
        Assert.Equal(1, reconnectingFired);
        Assert.Equal(EngineState.Running, engine.State);
    }

    [Fact]
    public async Task Crash_RetriesUntilOneSucceeds()
    {
        // Initial connect (call 1) succeeds, reconnect attempts 1 and 2 (calls 2,3) fail,
        // attempt 3 (call 4) succeeds.
        var engine = new FakeEngine(call => call == 1 || call >= 4);
        var wd = new EngineWatchdog(engine, Instant());

        await engine.StartAsync("cfg");
        wd.Arm("cfg");
        engine.Crash();
        await wd.ReconnectTask!;

        Assert.Equal(4, engine.StartCalls);
        Assert.Equal(EngineState.Running, engine.State);
    }

    [Fact]
    public async Task Crash_AllAttemptsFail_GivesUp()
    {
        var engine = new FakeEngine(call => call == 1); // only the initial connect works
        var wd = new EngineWatchdog(engine, Instant(maxAttempts: 3));
        bool gaveUp = false;
        wd.GaveUp += () => gaveUp = true;

        await engine.StartAsync("cfg");
        wd.Arm("cfg");
        engine.Crash();
        await wd.ReconnectTask!;

        Assert.True(gaveUp);
        Assert.Equal(1 + 3, engine.StartCalls); // initial + 3 failed attempts, then stop
        Assert.Equal(EngineState.Faulted, engine.State);
    }

    [Fact]
    public async Task Crash_WhenNotArmed_DoesNothing()
    {
        var engine = new FakeEngine(_ => true);
        var wd = new EngineWatchdog(engine, Instant());

        await engine.StartAsync("cfg");
        // not armed
        engine.Crash();

        Assert.Null(wd.ReconnectTask);
        Assert.Equal(1, engine.StartCalls);
    }

    [Fact]
    public async Task Disarm_StopsReconnectAttempts()
    {
        // Delay blocks until we release it, so we can disarm mid-backoff.
        var release = new TaskCompletionSource();
        var opt = new WatchdogOptions
        {
            MaxAttempts = 5,
            Backoff = _ => TimeSpan.Zero,
            Delay = (_, ct) => release.Task.WaitAsync(ct),
        };
        var engine = new FakeEngine(_ => true);
        var wd = new EngineWatchdog(engine, opt);

        await engine.StartAsync("cfg");
        wd.Arm("cfg");
        engine.Crash();          // enters loop, blocks in Delay
        wd.Disarm();             // cancels the loop
        release.TrySetResult();  // unblock (already cancelled)
        await wd.ReconnectTask!;

        Assert.Equal(1, engine.StartCalls); // no reconnect happened
    }

    [Fact]
    public async Task GivenUp_ThenManualReconnect_ReArms()
    {
        var engine = new FakeEngine(call => call == 1); // initial ok, reconnects fail
        var wd = new EngineWatchdog(engine, Instant(maxAttempts: 2));

        await engine.StartAsync("cfg");
        wd.Arm("cfg");
        engine.Crash();
        await wd.ReconnectTask!;   // gives up after 2

        // A later crash must not trigger another loop until re-armed.
        engine.Crash();
        Assert.Equal(1 + 2, engine.StartCalls);
    }
}
