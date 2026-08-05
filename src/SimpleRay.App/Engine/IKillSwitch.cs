namespace SimpleRay.App.Engine;

/// <summary>
/// Blocks network traffic from leaking around the tunnel while a connection is intended
/// (including the window when sing-box has crashed and the watchdog is reconnecting).
/// Engaged after a successful connect and disengaged on user disconnect / give-up / exit.
///
/// Implementations must fail safe: if the block can't be installed, connecting should still
/// proceed (degraded, no kill-switch) rather than the app becoming unusable, and the block
/// must never outlive the app in a way that leaves the machine with no network.
/// </summary>
public interface IKillSwitch : IDisposable
{
    bool IsEngaged { get; }

    /// <summary>
    /// Start blocking non-tunnel traffic. <paramref name="tunInterfaceName"/> is the VPN TUN
    /// adapter to permit; <paramref name="allowedExePath"/> is sing-box, permitted so it can
    /// reach the server over the physical link. Must be safe to call when already engaged.
    /// </summary>
    void Engage(string tunInterfaceName, string allowedExePath);

    /// <summary>Remove the block. Must be safe to call when not engaged.</summary>
    void Disengage();
}

/// <summary>No-op kill-switch: the default until a verified platform implementation is wired in.</summary>
public sealed class NoOpKillSwitch : IKillSwitch
{
    public bool IsEngaged { get; private set; }
    public void Engage(string tunInterfaceName, string allowedExePath) => IsEngaged = true;
    public void Disengage() => IsEngaged = false;
    public void Dispose() => Disengage();
}
