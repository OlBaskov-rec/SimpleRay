using SimpleRay.Core.Engine;

namespace SimpleRay.App.Engine;

public sealed class WatchdogOptions
{
    /// <summary>How many restart attempts before giving up.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Backoff before attempt n (1-based): 2s, 4s, 8s, 16s, capped at 30s.</summary>
    public Func<int, TimeSpan> Backoff { get; init; } =
        n => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, n)));

    /// <summary>Delay primitive, injectable so tests run without real waits.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } =
        (d, ct) => Task.Delay(d, ct);
}

/// <summary>
/// Restarts the engine after an unexpected fault (sing-box crashed / was killed), with
/// bounded backoff. Only active between <see cref="Arm"/> (called once a connection is
/// established) and <see cref="Disarm"/> (user disconnect), so a failed manual connect or
/// a clean stop never triggers a reconnect.
/// </summary>
public sealed class EngineWatchdog
{
    private readonly IVpnEngine _engine;
    private readonly WatchdogOptions _opt;

    private string? _configJson;
    private volatile bool _armed;
    private volatile bool _reconnecting;
    private CancellationTokenSource? _cts;

    public EngineWatchdog(IVpnEngine engine, WatchdogOptions? options = null)
    {
        _engine = engine;
        _opt = options ?? new WatchdogOptions();
        _engine.StateChanged += OnStateChanged;
    }

    /// <summary>Fired before each restart attempt: (attempt, maxAttempts).</summary>
    public event Action<int, int>? Reconnecting;

    /// <summary>Fired when all attempts failed and the watchdog stopped trying.</summary>
    public event Action? GaveUp;

    /// <summary>The in-progress reconnect loop, exposed so tests can await it. Null when idle.</summary>
    public Task? ReconnectTask { get; private set; }

    /// <summary>Arm auto-reconnect for the given config (call after a successful connect).</summary>
    public void Arm(string configJson)
    {
        _configJson = configJson;
        _armed = true;
    }

    /// <summary>Stop auto-reconnect and cancel any in-progress attempts (call on user disconnect).</summary>
    public void Disarm()
    {
        _armed = false;
        _cts?.Cancel();
    }

    private void OnStateChanged(object? sender, EngineState state)
    {
        // React only to an unexpected fault while armed, and never re-enter while a
        // reconnect loop is already running (a failed attempt re-faults the engine).
        if (state == EngineState.Faulted && _armed && !_reconnecting)
            ReconnectTask = ReconnectLoopAsync();
    }

    private async Task ReconnectLoopAsync()
    {
        _reconnecting = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            for (int attempt = 1; attempt <= _opt.MaxAttempts && _armed; attempt++)
            {
                Reconnecting?.Invoke(attempt, _opt.MaxAttempts);

                try { await _opt.Delay(_opt.Backoff(attempt), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                if (!_armed || ct.IsCancellationRequested) return;

                try
                {
                    await _engine.StartAsync(_configJson!, ct).ConfigureAwait(false);
                    if (_engine.State == EngineState.Running)
                        return; // recovered; the normal Running state drives the UI
                }
                catch (OperationCanceledException) { return; }
                catch { /* attempt failed — fall through to the next one */ }
            }

            if (_armed)
            {
                _armed = false; // give up; the user must reconnect manually
                GaveUp?.Invoke();
            }
        }
        finally
        {
            _reconnecting = false;
        }
    }
}
