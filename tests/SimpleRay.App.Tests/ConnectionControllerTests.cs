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
        private readonly Func<int, bool> _startSucceeds;
        public int StartCalls { get; private set; }

        public FakeEngine(Func<int, bool> startSucceeds) => _startSucceeds = startSucceeds;

        public EngineState State { get; private set; } = EngineState.Stopped;
        public event EventHandler<EngineState>? StateChanged;
#pragma warning disable CS0067 // required by IVpnEngine; this fake never emits logs
        public event EventHandler<string>? LogReceived;
#pragma warning restore CS0067

        public Task StartAsync(string configJson, CancellationToken ct = default)
        {
            StartCalls++;
            SetState(EngineState.Starting);
            if (_startSucceeds(StartCalls)) { SetState(EngineState.Running); return Task.CompletedTask; }
            SetState(EngineState.Faulted);
            throw new InvalidOperationException("start failed");
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

    // Instant watchdog: no real waits, so GaveUp is reached synchronously in tests.
    private static WatchdogOptions Instant(int maxAttempts = 2) => new()
    {
        MaxAttempts = maxAttempts,
        Backoff = _ => TimeSpan.Zero,
        Delay = (_, _) => Task.CompletedTask,
        StabilityWindow = TimeSpan.Zero,
    };

    private static ConnectionController Make(FakeEngine engine, IKillSwitch ks, Func<bool> wanted,
        WatchdogOptions? wd = null) =>
        new(engine, ks, wanted, livenessProbe: _ => Task.FromResult(true), watchdogOptions: wd);

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
}
