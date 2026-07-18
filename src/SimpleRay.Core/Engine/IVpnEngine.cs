namespace SimpleRay.Core.Engine;

public enum EngineState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

/// <summary>
/// Platform-neutral VPN engine contract. The Windows implementation drives a
/// sing-box child process; an Android implementation would drive VpnService with
/// the sing-box library. Callers (view models) depend on this abstraction, never
/// on a concrete engine, so <see cref="SimpleRay.Core"/> stays free of the
/// desktop process model.
/// </summary>
public interface IVpnEngine : IAsyncDisposable
{
    EngineState State { get; }

    event EventHandler<EngineState>? StateChanged;
    event EventHandler<string>? LogReceived;

    /// <summary>Starts the engine with the given sing-box config JSON.</summary>
    Task StartAsync(string configJson, CancellationToken ct = default);

    /// <summary>Stops the engine, cleanly if possible.</summary>
    Task StopAsync();
}
