using System;
using System.Threading;
using System.Threading.Tasks;
using SimpleRay.App.Engine;
using SimpleRay.Core.Engine;

namespace SimpleRay.App.Tests;

public class ConnectionControllerTests
{
    /// <summary>IVpnEngine whose StartAsync outcome is scripted per call.</summary>
    private sealed class FakeEngine : IVpnEngine
    {
        private readonly Func<int, string?> _script; // per call: null = success, else throw this message
        private TaskCompletionSource? _startGate;

        public int StartCalls { get; private set; }

        public void SetStartGate(TaskCompletionSource gate) => _startGate = gate;

        public FakeEngine(Func<int, bool> startSucceeds) : this(c => startSucceeds(c) ? null : "start failed") { }

        public FakeEngine(Func<int, string?> script) => _script = script;

        public EngineState State { get; private set; } = EngineState.Stopped;
        public event EventHandler<EngineState>? StateChanged;
#pragma warning disable CS0067 // required by IVpnEngine; this fake never emits logs
        public event EventHandler<string>? LogReceived;
#pragma warning restore CS0067

        public Task StartAsync(string configJson, CancellationToken ct = default)
        {
            StartCalls++;
            SetState(EngineState.Starting);
            var fail = _script(StartCalls);
            if (_startGate is not null) { _startGate.Task.Wait(ct); }
            if (fail is null) { SetState(EngineState.Running); return Task.CompletedTask; }
            SetState(EngineState.Faulted);
            throw new InvalidOperationException(fail);
        }

        public Task StopAsync() { SetState(EngineState.Stopped); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { SetState(EngineState.Stopped); return default; }
        public void Crash() => SetState(EngineState.Faulted);
        private void SetState(EngineState s) { State = s; StateChanged?.Invoke(this, s); }
    }

    /// <summary>IKillSwitch that counts Engage/Disengage and tracks IsEngaged.</summary>
    private sealed class CountingKillSwitch : IKillSwitch
    {
        public int EngageCount { get; private set; }
        public int DisengageCount { get; private set; }
        public bool IsEngaged { get; private set; }
        public void Engage(string tun, string exe) { EngageCount++; IsEngaged = true; }
        public void Disengage() { DisengageCount++; IsEngaged = false; }
        public void Dispose() => Disengage();
    }

    /// <summary>IKillSwitch whose Engage always throws (WFP failure).</summary>
    private sealed class ThrowingKillSwitch : IKillSwitch
    {
        public bool IsEngaged => false;
        public void Engage(string tun, string exe) => throw new InvalidOperationException("WFP boom");
        public void Disengage() { }
        public void Dispose() { }
    }

    // Instant watchdog: no real waits, so GaveUp is reached synchronously in tests.
    private static WatchdogOptions Instant(int maxAttempts = 2) => new()
    {
        MaxAttempts = maxAttempts,
        Backoff = _ => TimeSpan.Zero,
        Delay = (_, _) => Task.CompletedTask,
        StabilityWindow = TimeSpan.Zero,
    };

    // `wanted` is vestigial (kept so call sites stay unchanged): give-up now decides on the
    // kill-switch's ACTUAL IsEngaged state, driven by the ConnectAsync killSwitch flag.
    private static ConnectionController Make(FakeEngine engine, IKillSwitch ks, Func<bool> wanted,
        WatchdogOptions? wd = null, TimeSpan? retryDelay = null) =>
        new(engine, ks, livenessProbe: _ => Task.FromResult(true), watchdogOptions: wd,
            retryDelay: retryDelay ?? TimeSpan.Zero); // no real waits between start retries in tests

    private const string Core = "sr-fake-engine.exe"; // KillStale finds no such process -> no-op

    [Fact]
    public async Task ConnectAsync_Starts_Arms_And_EngagesKillSwitch()
    {
        var engine = new FakeEngine(_ => true);
        var ks = new CountingKillSwitch();
        var c = Make(engine, ks, () => true);

        await c.ConnectAsync("cfg", "simpleray", Core, killSwitch: true);

        Assert.Equal(1, engine.StartCalls);
        Assert.Equal(EngineState.Running, engine.State);
        Assert.Equal(1, ks.EngageCount);
        Assert.True(ks.IsEngaged);
    }

    [Fact]
    public async Task ConnectAsync_WithoutKillSwitch_DoesNotEngage()
    {
        var engine = new FakeEngine(_ => true);
        var ks = new CountingKillSwitch();
        var c = Make(engine, ks, () => false);

        await c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false);

        Assert.Equal(EngineState.Running, engine.State);
        Assert.Equal(0, ks.EngageCount);
    }

    [Fact]
    public async Task DisconnectAsync_Disengages_Stops_AndDisarms()
    {
        var engine = new FakeEngine(_ => true);
        var ks = new CountingKillSwitch();
        var c = Make(engine, ks, () => true);

        await c.ConnectAsync("cfg", "simpleray", Core, killSwitch: true);
        await c.DisconnectAsync();

        Assert.Equal(EngineState.Stopped, engine.State);
        Assert.True(ks.DisengageCount >= 1);
        Assert.False(ks.IsEngaged);

        // Disarmed: a later fault must NOT trigger a reconnect.
        engine.Crash();
        Assert.Equal(1, engine.StartCalls);
    }

    [Fact]
    public async Task GaveUp_WithKillSwitchWanted_KeepsBlocking()
    {
        var engine = new FakeEngine(call => call == 1); // initial ok, reconnects fail
        var ks = new CountingKillSwitch();
        var c = Make(engine, ks, () => true, Instant());
        var blocked = new TaskCompletionSource();
        var disengaged = false;
        c.GaveUpBlocked += () => blocked.TrySetResult();
        c.GaveUpDisengaged += () => disengaged = true;

        await c.ConnectAsync("cfg", "simpleray", Core, killSwitch: true);
        int engagedBefore = ks.DisengageCount;
        engine.Crash();

        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(disengaged);
        Assert.Equal(engagedBefore, ks.DisengageCount); // fail-closed: NOT disengaged
    }

    [Fact]
    public async Task GaveUp_WithoutKillSwitch_Disengages()
    {
        var engine = new FakeEngine(call => call == 1);
        var ks = new CountingKillSwitch();
        var c = Make(engine, ks, () => false, Instant());
        var disengaged = new TaskCompletionSource();
        c.GaveUpDisengaged += () => disengaged.TrySetResult();

        await c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false);
        engine.Crash();

        await disengaged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(ks.DisengageCount >= 1);
    }

    // --- characterization of the async start/reconnect lifecycle ---------

    [Fact]
    public async Task ConnectAsync_RetriesOnceOnExitedImmediately_ThenSucceeds()
    {
        // First start fails with the "exited immediately" signature (stale TUN adapter); retry works.
        var engine = new FakeEngine(call => call == 1 ? "sing-box exited immediately (code 1)" : null);
        var c = Make(engine, new CountingKillSwitch(), () => false);

        await c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false);

        Assert.Equal(2, engine.StartCalls); // retried once, then up
        Assert.Equal(EngineState.Running, engine.State);
    }

    [Fact]
    public async Task ConnectAsync_ExitedImmediatelyEveryTime_GivesUpAfterThreeAndThrows()
    {
        var engine = new FakeEngine(_ => "sing-box exited immediately (code 1)");
        var c = Make(engine, new CountingKillSwitch(), () => false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false));

        Assert.Equal(3, engine.StartCalls); // bounded: 3 attempts, then surfaces the error
    }

    [Fact]
    public async Task ConnectAsync_NonRetryableStartFailure_ThrowsWithoutRetrying()
    {
        // A start failure that is NOT "exited immediately" must not be retried.
        var engine = new FakeEngine(_ => "invalid configuration: something");
        var c = Make(engine, new CountingKillSwitch(), () => false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false));

        Assert.Equal(1, engine.StartCalls); // no retry
    }

    [Fact]
    public async Task ConnectAsync_KillSwitchEngageFails_RaisesEventAndStaysConnected()
    {
        var engine = new FakeEngine(_ => (string?)null); // engine starts fine
        var c = Make(engine, new ThrowingKillSwitch(), () => true);
        Exception? reported = null;
        c.KillSwitchEngageFailed += ex => reported = ex;

        await c.ConnectAsync("cfg", "simpleray", Core, killSwitch: true); // must NOT throw

        Assert.Equal(EngineState.Running, engine.State); // connection stays up (degraded)
        Assert.NotNull(reported);                        // failure surfaced via the event, not thrown
    }

    [Fact]
    public async Task Crash_AfterConnect_AutoReconnects()
    {
        var engine = new FakeEngine(_ => (string?)null); // every start succeeds
        var c = Make(engine, new CountingKillSwitch(), () => false, Instant());

        await c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false); // Running (call 1)

        var reconnected = new TaskCompletionSource();
        c.StateChanged += (_, s) => { if (s == EngineState.Running) reconnected.TrySetResult(); };
        engine.Crash(); // Faulted -> watchdog restarts

        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, engine.StartCalls); // initial + one auto-reconnect
        Assert.Equal(EngineState.Running, engine.State);
    }

    [Fact]
    public async Task DisconnectDuringRetry_DoesNotResurrect()
    {
        var engine = new FakeEngine(call => call == 1 ? "sing-box exited immediately (code 1)" : null);
        var ks = new CountingKillSwitch();
        var c = Make(engine, ks, () => false, retryDelay: TimeSpan.FromSeconds(10));

        var connectTask = c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (engine.StartCalls < 1 && sw.ElapsedMilliseconds < 2000) { await Task.Delay(10); }
        Assert.Equal(1, engine.StartCalls);

        await c.DisconnectAsync();

        var exThrown = false;
        try { await connectTask; }
        catch (OperationCanceledException) { exThrown = true; }
        Assert.True(exThrown || connectTask.IsCanceled);
        Assert.Equal(1, engine.StartCalls);
        Assert.NotEqual(EngineState.Running, engine.State);
    }

    [Fact]
    public async Task ConcurrentConnect_IsSingleFlight()
    {
        var startGate = new TaskCompletionSource();
        var engine = new FakeEngine(_ => (string?)null);
        engine.SetStartGate(startGate);
        var c = Make(engine, new CountingKillSwitch(), () => false);

        var t1 = c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false);
        await Task.Delay(50); // give t1 time to enter StartAsync
        var t2 = c.ConnectAsync("cfg", "simpleray", Core, killSwitch: false);

        Assert.Same(t1, t2);
        Assert.Equal(1, engine.StartCalls);

        startGate.TrySetResult();
        await t1;
        Assert.Equal(1, engine.StartCalls);
    }

    [Fact]
    public async Task DisconnectAsync_Idempotent()
    {
        var engine = new FakeEngine(_ => (string?)null);
        var ks = new CountingKillSwitch();
        var c = Make(engine, ks, () => false);

        await c.DisconnectAsync();
        await c.DisconnectAsync();

        Assert.Equal(EngineState.Stopped, engine.State);
    }
}
