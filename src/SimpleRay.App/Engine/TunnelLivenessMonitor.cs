namespace SimpleRay.App.Engine;

/// <summary>
/// Periodically probes end-to-end connectivity THROUGH the tunnel. After
/// <see cref="_threshold"/> consecutive failed probes it fires <c>onDead</c> so the caller can
/// force a reconnect — catching a "silent" tunnel death (sing-box process still alive but the
/// connection is gone), which the process-level <see cref="EngineWatchdog"/> cannot see.
///
/// A single success resets the failure count, so a transient hiccup doesn't reconnect. The
/// probe and delay are injectable so the loop is testable without real network or waits.
/// </summary>
public sealed class TunnelLivenessMonitor
{
    private readonly Func<CancellationToken, Task<bool>> _probe;
    private readonly Func<CancellationToken, Task> _onDead;
    private readonly TimeSpan _interval;
    private readonly int _threshold;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private CancellationTokenSource? _cts;

    /// <summary>The running probe loop, exposed so tests can await it. Null when stopped.</summary>
    public Task? LoopTask { get; private set; }

    public TunnelLivenessMonitor(
        Func<CancellationToken, Task<bool>> probe,
        Func<CancellationToken, Task> onDead,
        TimeSpan interval,
        int threshold,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _probe = probe;
        _onDead = onDead;
        _interval = interval;
        _threshold = Math.Max(1, threshold);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>(Re)start monitoring. Safe to call when already running — it restarts the loop.</summary>
    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        LoopTask = RunAsync(_cts.Token);
    }

    /// <summary>Stop monitoring (call on disconnect / give-up / exit).</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try { await _delay(_interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;

            bool ok;
            try { ok = await _probe(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch { ok = false; }

            if (ok) { failures = 0; continue; }

            if (++failures >= _threshold)
            {
                failures = 0; // let onDead reconnect; keep monitoring for the next death
                try { await _onDead(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch { /* ignore, keep monitoring */ }
            }
        }
    }
}
