using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SimpleRay.App.Engine;
using SimpleRay.Core.Engine;

namespace SimpleRay.App.Tests;

/// <summary>
/// Exercises the SingBoxEngine start/stop/fault state machine through a fake process, so
/// the lifecycle is covered without launching a real sing-box.
/// </summary>
public class SingBoxEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "srengine_" + Guid.NewGuid().ToString("N"));
    private readonly string _exe;

    public SingBoxEngineTests()
    {
        Directory.CreateDirectory(_dir);
        _exe = Path.Combine(_dir, "sing-box.exe");
        File.WriteAllText(_exe, "stub"); // EnsureFilesExist only checks presence
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    // --- test doubles -----------------------------------------------------

    private sealed class FakeProcess : IEngineProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StartResult { get; set; } = true;
        public bool ExitImmediately { get; set; }
        public int ImmediateExitCode { get; set; } = 1;
        public List<string> OutputOnStart { get; } = new();

        public bool StartCalled { get; private set; }
        public bool KillCalled { get; private set; }
        public bool Disposed { get; private set; }

        public int Id => 4242;
        public bool HasExited { get; private set; }
        public int ExitCode { get; private set; }

        public event EventHandler? Exited;
        public event Action<string>? OutputReceived;

        public bool Start()
        {
            StartCalled = true;
            if (!StartResult) return false;
            foreach (var line in OutputOnStart) OutputReceived?.Invoke(line);
            if (ExitImmediately) MarkExited(ImmediateExitCode);
            return true;
        }

        public void Kill()
        {
            KillCalled = true;
            MarkExited(137);
        }

        public Task WaitForExitAsync(CancellationToken ct = default) => _exit.Task.WaitAsync(ct);
        public void Dispose() => Disposed = true;

        public void Emit(string line) => OutputReceived?.Invoke(line);

        /// <summary>Simulate an unexpected process exit (raises Exited).</summary>
        public void RaiseExit(int code)
        {
            MarkExited(code);
            Exited?.Invoke(this, EventArgs.Empty);
        }

        private void MarkExited(int code)
        {
            if (HasExited) return;
            ExitCode = code;
            HasExited = true;
            _exit.TrySetResult();
        }
    }

    private sealed class FakeLauncher : IProcessLauncher
    {
        private readonly Queue<FakeProcess> _queue = new();
        public List<string> CreatedArgs { get; } = new();

        public FakeLauncher(params FakeProcess[] processes)
        {
            foreach (var p in processes) _queue.Enqueue(p);
        }

        public IEngineProcess Create(string fileName, string arguments, string workingDirectory)
        {
            CreatedArgs.Add(arguments);
            if (_queue.Count == 0)
                throw new InvalidOperationException("No fake process queued for: " + arguments);
            return _queue.Dequeue();
        }
    }

    private sealed class FakeTerminator : IProcessTerminator
    {
        public bool Result { get; set; }
        public bool Throw { get; set; }
        public bool Called { get; private set; }

        public Task<bool> TryGracefulStopAsync(IEngineProcess process, TimeSpan timeout)
        {
            Called = true;
            if (Throw) throw new InvalidOperationException("terminator boom");
            return Task.FromResult(Result);
        }
    }

    private EngineOptions Options(FakeLauncher launcher, bool validate = false, IProcessTerminator? terminator = null) => new()
    {
        ExecutablePath = _exe,
        WorkingDirectory = _dir,
        ValidateBeforeStart = validate,
        Terminator = terminator,
        ProcessLauncher = launcher,
        StartupProbeDelay = TimeSpan.FromMilliseconds(20),
    };

    private static FakeProcess Running() => new();                       // stays up
    private static FakeProcess ExitsWith(int code, params string[] output)
    {
        var p = new FakeProcess { ExitImmediately = true, ImmediateExitCode = code };
        p.OutputOnStart.AddRange(output);
        return p;
    }

    // --- state machine ----------------------------------------------------

    [Fact]
    public async Task Start_Success_ReachesRunning()
    {
        var run = Running();
        var engine = new SingBoxEngine(Options(new FakeLauncher(run)));
        var states = new List<EngineState>();
        engine.StateChanged += (_, s) => states.Add(s);

        await engine.StartAsync("{}");

        Assert.Equal(EngineState.Running, engine.State);
        Assert.Contains(EngineState.Starting, states);
        Assert.Contains(EngineState.Running, states);
        Assert.True(run.StartCalled);
    }

    [Fact]
    public async Task Start_LaunchFails_Faults()
    {
        var run = new FakeProcess { StartResult = false };
        var engine = new SingBoxEngine(Options(new FakeLauncher(run)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync("{}"));

        Assert.Equal(EngineState.Faulted, engine.State);
        Assert.True(run.Disposed);
    }

    [Fact]
    public async Task Start_ProcessExitsImmediately_Faults()
    {
        var engine = new SingBoxEngine(Options(new FakeLauncher(ExitsWith(1))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync("{}"));

        Assert.Contains("exited immediately", ex.Message);
        Assert.Equal(EngineState.Faulted, engine.State);
    }

    [Fact]
    public async Task Start_WhenAlreadyRunning_Throws()
    {
        var engine = new SingBoxEngine(Options(new FakeLauncher(Running())));
        await engine.StartAsync("{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync("{}"));
        Assert.Equal(EngineState.Running, engine.State);
    }

    [Fact]
    public async Task Stop_WithGracefulTerminator_DoesNotKill()
    {
        var run = Running();
        var term = new FakeTerminator { Result = true };
        var engine = new SingBoxEngine(Options(new FakeLauncher(run), terminator: term));
        await engine.StartAsync("{}");

        await engine.StopAsync();

        Assert.True(term.Called);
        Assert.False(run.KillCalled);       // graceful stop => no hard kill
        Assert.True(run.Disposed);
        Assert.Equal(EngineState.Stopped, engine.State);
    }

    [Fact]
    public async Task Stop_NoTerminator_HardKills()
    {
        var run = Running();
        var engine = new SingBoxEngine(Options(new FakeLauncher(run)));
        await engine.StartAsync("{}");

        await engine.StopAsync();

        Assert.True(run.KillCalled);
        Assert.Equal(EngineState.Stopped, engine.State);
    }

    [Fact]
    public async Task Stop_TerminatorFailsOrThrows_FallsBackToKill()
    {
        foreach (var term in new[] { new FakeTerminator { Result = false }, new FakeTerminator { Throw = true } })
        {
            var run = Running();
            var engine = new SingBoxEngine(Options(new FakeLauncher(run), terminator: term));
            await engine.StartAsync("{}");

            await engine.StopAsync();

            Assert.True(term.Called);
            Assert.True(run.KillCalled);    // graceful failed => hard kill
            Assert.Equal(EngineState.Stopped, engine.State);
        }
    }

    [Fact]
    public async Task Stop_WhenNeverStarted_IsStopped()
    {
        var engine = new SingBoxEngine(Options(new FakeLauncher()));
        await engine.StopAsync();
        Assert.Equal(EngineState.Stopped, engine.State);
    }

    [Fact]
    public async Task UnexpectedExit_WhileRunning_Faults()
    {
        var run = Running();
        var engine = new SingBoxEngine(Options(new FakeLauncher(run)));
        await engine.StartAsync("{}");
        Assert.Equal(EngineState.Running, engine.State);

        run.RaiseExit(1); // the process dies on its own

        Assert.Equal(EngineState.Faulted, engine.State);
    }

    [Fact]
    public async Task Output_IsForwardedToLog()
    {
        var run = Running();
        var engine = new SingBoxEngine(Options(new FakeLauncher(run)));
        var log = new List<string>();
        engine.LogReceived += (_, line) => log.Add(line);

        await engine.StartAsync("{}");
        run.Emit("hello from sing-box");

        Assert.Contains("hello from sing-box", log);
    }

    // --- validation ordering (no binary needed) ---------------------------

    [Fact]
    public async Task Validate_InvalidConfig_ThrowsBeforeSpawningRun()
    {
        // Only a check process is queued; if the engine tried to spawn "run", the launcher
        // would throw "no fake queued" — so this also proves run is never reached.
        var check = ExitsWith(1, "FATAL[0000] bad config");
        var launcher = new FakeLauncher(check);
        var engine = new SingBoxEngine(Options(launcher, validate: true));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync("{}"));

        Assert.Contains("Invalid configuration", ex.Message);
        Assert.Single(launcher.CreatedArgs);                 // only the check ran
        Assert.Contains("check", launcher.CreatedArgs[0]);
        Assert.NotEqual(EngineState.Running, engine.State);
    }

    [Fact]
    public async Task Validate_ValidConfig_ProceedsToRun()
    {
        var check = ExitsWith(0);          // check passes
        var run = Running();
        var launcher = new FakeLauncher(check, run);
        var engine = new SingBoxEngine(Options(launcher, validate: true));

        await engine.StartAsync("{}");

        Assert.Equal(EngineState.Running, engine.State);
        Assert.Equal(2, launcher.CreatedArgs.Count);
        Assert.Contains("check", launcher.CreatedArgs[0]);   // check first,
        Assert.Contains("run", launcher.CreatedArgs[1]);     // then run
    }

    [Fact]
    public async Task CheckConfig_ReturnsExitAndOutput()
    {
        var check = ExitsWith(1, "FATAL[0000] decode config: boom");
        var engine = new SingBoxEngine(Options(new FakeLauncher(check)));

        var (ok, output) = await engine.CheckConfigAsync("{}");

        Assert.False(ok);
        Assert.Contains("decode config", output);
    }
}
