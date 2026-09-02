using System.Net.Http;
using SimpleRay.Core.Engine;

namespace SimpleRay.App.Engine;

/// <summary>
/// Owns the VPN connection lifecycle — the engine plus the watchdog, the liveness monitor and
/// the kill-switch — behind a small, UI-agnostic surface (<see cref="ConnectAsync"/> /
/// <see cref="DisconnectAsync"/> / <see cref="ShutdownAsync"/> and a handful of events). The
/// view-model keeps only UI concerns (status text, tray, profile highlight) and maps these
/// events onto them. Extracted from MainViewModel to give this behaviour-sensitive flow a
/// testable seam; the call sequence is unchanged from the original inline orchestration.
/// </summary>
public sealed class ConnectionController
{
    private readonly IVpnEngine _engine;
    private readonly IKillSwitch _killSwitch;
    private readonly EngineWatchdog _watchdog;
    private readonly TunnelLivenessMonitor _liveness;

    private readonly TimeSpan _retryDelay;

    // Synchronization: _gate serializes access to the engine, _sync protects CTS/Task state
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _connectCts;
    private Task? _connectTask;

    public ConnectionController(
        IVpnEngine engine,
        IKillSwitch killSwitch,
        Func<CancellationToken, Task<bool>>? livenessProbe = null,
        WatchdogOptions? watchdogOptions = null,
        TimeSpan? retryDelay = null)
    {
        _engine = engine;
        _killSwitch = killSwitch;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(1200); // pause before a start retry (injectable for tests)

        // Forward the engine's own events so the view-model subscribes here, not to the engine.
        _engine.StateChanged += (_, state) => StateChanged?.Invoke(this, state);
        _engine.LogReceived += (_, line) => LogReceived?.Invoke(this, line);

        _watchdog = watchdogOptions is null ? new EngineWatchdog(_engine) : new EngineWatchdog(_engine, watchdogOptions);

        // Detect a "silent" tunnel death (process alive, connection gone) the process watchdog
        // can't see, and force a reconnect through it.
        _liveness = new TunnelLivenessMonitor(
            probe: livenessProbe ?? ProbeTunnelAsync,
            onDead: _ => { _watchdog.NotifyUnhealthy(); return Task.CompletedTask; },
            interval: TimeSpan.FromSeconds(20),
            threshold: 3);

        _watchdog.Reconnecting += (attempt, max) => Reconnecting?.Invoke(attempt, max);
        _watchdog.GaveUp += () =>
        {
            // Decide on the ACTUAL kill-switch state, not the checkbox intent: if the user toggled
            // the setting on mid-session (no Engage ran), the filters aren't installed, so reporting
            // "blocked" would be a false sense of protection.
            if (_killSwitch.IsEngaged)
            {
                // Fail-closed: keep blocking after we give up, so the tunnel dropping never leaks
                // the real IP (the whole point of the kill-switch). The user reconnects manually.
                GaveUpBlocked?.Invoke();
            }
            else
            {
                _killSwitch.Disengage(); // no kill-switch: fall back to direct so the user isn't left offline
                GaveUpDisengaged?.Invoke();
            }
            _liveness.Stop(); // gave up — stop probing (a manual reconnect restarts it)
        };
    }

    /// <summary>Engine state transitions (forwarded from the engine).</summary>
    public event EventHandler<EngineState>? StateChanged;

    /// <summary>Merged stdout/stderr lines (forwarded from the engine).</summary>
    public event EventHandler<string>? LogReceived;

    /// <summary>A reconnect attempt is starting: (attempt, maxAttempts).</summary>
    public event Action<int, int>? Reconnecting;

    /// <summary>Reconnect gave up while the kill-switch stays engaged (fail-closed, no leak).</summary>
    public event Action? GaveUpBlocked;

    /// <summary>Reconnect gave up with no kill-switch; traffic fell back to direct.</summary>
    public event Action? GaveUpDisengaged;

    /// <summary>Kill-switch Engage failed; the connection stays up but is unprotected.</summary>
    public event Action<Exception>? KillSwitchEngageFailed;

    /// <summary>
    /// Starts the tunnel for the given config, arms auto-reconnect + liveness, and (when
    /// <paramref name="killSwitch"/>) engages the kill-switch off the UI thread. A kill-switch
    /// failure is surfaced via <see cref="KillSwitchEngageFailed"/> and never fails the connect;
    /// an engine start failure propagates to the caller.
    /// </summary>
    public Task ConnectAsync(string configJson, string tunInterfaceName, string coreExePath, bool killSwitch)
    {
        lock (_sync)
        {
            if (_connectTask is not null && !_connectTask.IsCompleted)
                return _connectTask; // single-flight: coalesce a re-entrant connect

            _connectCts = new CancellationTokenSource();
            // Publish the task under the lock so a concurrent caller can't miss it and double-start.
            _connectTask = ConnectCoreAsync(configJson, tunInterfaceName, coreExePath, killSwitch, _connectCts.Token);
            return _connectTask;
        }
    }

    /// <summary>
    /// Internal implementation of ConnectAsync: acquires the gate, starts the engine with retry,
    /// arms watchdog/liveness, engages kill-switch, and cleans up on cancellation.
    /// </summary>
    private async Task ConnectCoreAsync(string configJson, string tunInterfaceName, string coreExePath, bool killSwitch, CancellationToken token)
    {
        var cts = _connectCts; // capture for reference equality check in finally
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            await StartWithRetryAsync(configJson, coreExePath, token);
            token.ThrowIfCancellationRequested();
            _watchdog.Arm(configJson);
            _liveness.Start();
            if (killSwitch)
            {
                try
                {
                    await Task.Run(() => _killSwitch.Engage(tunInterfaceName, coreExePath), token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { KillSwitchEngageFailed?.Invoke(ex); }
            }
        }
        finally
        {
            _gate.Release();
            lock (_sync)
            {
                if (ReferenceEquals(_connectCts, cts))
                {
                    _connectCts = null;
                    _connectTask = null;
                }
            }
            cts?.Dispose();
        }
    }


    /// <summary>User-initiated stop: no auto-reconnect, stop probing, stop blocking, stop the engine.</summary>
    public async Task DisconnectAsync()
    {
        Task? inflight;
        lock (_sync)
        {
            _connectCts?.Cancel();
            inflight = _connectTask;
        }

        if (inflight is not null)
        {
            try
            {
                await inflight.ConfigureAwait(false);
            }
            catch { }
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _watchdog.Disarm();
            _liveness.Stop();
            _killSwitch.Disengage();
            await _engine.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }


    /// <summary>
    /// Tears the connection down and disposes the engine. Deliberately does not await the
    /// disposal itself (returns its task) so a caller blocking on it from the UI thread — e.g.
    /// MainWindow.Closed — doesn't deadlock; the engine uses ConfigureAwait(false) throughout.
    /// </summary>
    public Task ShutdownAsync()
    {
        lock (_sync)
        {
            _connectCts?.Cancel();
        }

        _watchdog.Disarm();
        _liveness.Stop();
        _killSwitch.Disengage();
        return _engine.DisposeAsync().AsTask();
    }


    /// <summary>
    /// Starts the engine, first clearing any orphaned engine still holding the TUN adapter, and
    /// retrying briefly when the start fails immediately — most often the adapter from a
    /// just-killed engine hasn't been released yet ("Cannot create ... already exists").
    /// </summary>
    private async Task StartWithRetryAsync(string configJson, string coreExePath, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Run(() => EngineOrphans.KillStale(coreExePath), ct);
            try
            {
                await _engine.StartAsync(configJson, ct);
                return;
            }
            catch (InvalidOperationException ex)
                when (attempt < maxAttempts && ex.Message.Contains("exited immediately", StringComparison.Ordinal))
            {
                await Task.Delay(_retryDelay, ct); // give the stale TUN adapter time to be released, then retry (cancellable)
            }
        }
    }

    private static readonly HttpClient _livenessHttp = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>
    /// Liveness probe: a tiny request that must travel THROUGH the tunnel to succeed (foreign
    /// host → proxy in Rule/Global mode). Returns false on any failure so the monitor can react.
    /// </summary>
    private static async Task<bool> ProbeTunnelAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _livenessHttp.GetAsync(
                "https://www.gstatic.com/generate_204", HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            return (int)resp.StatusCode is 204 or 200;
        }
        catch { return false; }
    }
}
