using System.Runtime.InteropServices;
using SimpleRay.App.Engine;

namespace SimpleRay.App.Infrastructure;

/// <summary>
/// Windows graceful terminator. Attaches to the sing-box child console and sends
/// CTRL+C so sing-box runs its shutdown path and tears down the wintun adapter and
/// auto-added routes before exiting — avoiding stale routes after disconnect.
///
/// Best-effort: any failure (no console, attach denied, timeout) returns false so
/// <see cref="SingBoxEngine"/> falls back to a hard kill. Console attach/detach is a
/// process-global operation, so calls are serialized.
/// </summary>
public sealed class ConsoleCtrlTerminator : IProcessTerminator
{
    private const uint CTRL_C_EVENT = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<bool> TryGracefulStopAsync(IEngineProcess process, TimeSpan timeout)
    {
        if (process.HasExited) return true;

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Detach from any console of our own, then borrow the child's console.
            FreeConsole();
            if (!AttachConsole((uint)process.Id))
                return false;

            try
            {
                // Make sure the event we are about to raise does not terminate us.
                SetConsoleCtrlHandler(IntPtr.Zero, true);

                // group 0 = every process attached to this (the child's) console.
                if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0))
                    return false;

                using var cts = new CancellationTokenSource(timeout);
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return process.HasExited;
            }
            finally
            {
                SetConsoleCtrlHandler(IntPtr.Zero, false);
                FreeConsole();
            }
        }
        catch
        {
            return false; // timed out or failed → caller hard-kills
        }
        finally
        {
            Gate.Release();
        }
    }
}
