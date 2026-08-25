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
    private readonly Func<bool> _killSwitchWanted;
    private readonly EngineWatchdog _watchdog;
    private readonly TunnelLivenessMonitor _liveness;

    private readonly TimeSpan _retryDelay;

    public ConnectionController(
        IVpnEngine engine,
        IKillSwitch killSwitch,
        Func<bool> killSwitchWanted,
        Func<CancellationToken, Task<bool>>? livenessProbe = null,
        WatchdogOptions? watchdogOptions = null,
        TimeSpan? retryDelay = null)
    {
        _engine = engine;
        _killSwitch = killSwitch;
        _killSwitchWanted = killSwitchWanted;
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
            if (_killSwitchWanted())
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
    public async Task ConnectAsync(string configJson, string tunInterfaceName, string coreExePath, bool killSwitch)
    {
        await StartWithRetryAsync(configJson, coreExePath);
        _watchdog.Arm(configJson); // auto-reconnect this config if sing-box later crashes
        _liveness.Start();         // and force a reconnect if the tunnel silently dies
        if (killSwitch)            // block non-tunnel traffic while we intend to be connected
        {
            // Run off the UI thread: Engage waits a beat for the TUN adapter to appear (so
            // permit-tun is installed). A failure must not fail the connection.
            try { await Task.Run(() => _killSwitch.Engage(tunInterfaceName, coreExePath)); }
            catch (Exception ex) { KillSwitchEngageFailed?.Invoke(ex); }
        }
    }

    /// <summary>User-initiated stop: no auto-reconnect, stop probing, stop blocking, stop the engine.</summary>
    public async Task DisconnectAsync()
    {
        _watchdog.Disarm();      // user-initiated stop: don't auto-reconnect
        _liveness.Stop();        // and stop probing the tunnel
        _killSwitch.Disengage(); // and stop blocking non-tunnel traffic
        await _engine.StopAsync();
    }

    /// <summary>
    /// Tears the connection down and disposes the engine. Deliberately does not await the
    /// disposal itself (returns its task) so a caller blocking on it from the UI thread — e.g.
    /// MainWindow.Closed — doesn't deadlock; the engine uses ConfigureAwait(false) throughout.
    /// </summary>
    public Task ShutdownAsync()
    {
        _watchdog.Disarm();      // don't let a shutdown-time fault trigger a reconnect
        _liveness.Stop();        // stop probing
        _killSwitch.Disengage(); // never leave the machine blocked after we exit
        return _engine.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Starts the engine, first clearing any orphaned engine still holding the TUN adapter, and
    /// retrying briefly when the start fails immediately — most often the adapter from a
    /// just-killed engine hasn't been released yet ("Cannot create ... already exists").
    /// </summary>
    private async Task StartWithRetryAsync(string configJson, string coreExePath)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            await Task.Run(() => EngineOrphans.KillStale(coreExePath));
            try
            {
                await _engine.StartAsync(configJson);
                return;
            }
            catch (InvalidOperationException ex)
                when (attempt < maxAttempts && ex.Message.Contains("exited immediately", StringComparison.Ordinal))
            {
                await Task.Delay(_retryDelay); // give the stale TUN adapter time to be released, then retry
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
