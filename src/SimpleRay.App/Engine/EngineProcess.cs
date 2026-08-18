using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SimpleRay.App.Engine;

/// <summary>
/// An OS process the engine controls, abstracted so the engine's start/stop/fault state
/// machine can be unit-tested with a fake process — no real sing-box binary required.
/// </summary>
public interface IEngineProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int ExitCode { get; }

    /// <summary>Raised when the process exits (only observed once the caller subscribes).</summary>
    event EventHandler? Exited;

    /// <summary>A line of the process's merged stdout+stderr.</summary>
    event Action<string>? OutputReceived;

    /// <summary>Starts the process and begins reading its output. Returns false on launch failure.</summary>
    bool Start();

    /// <summary>Kills the process and its tree. May throw if it has already exited.</summary>
    void Kill();

    Task WaitForExitAsync(CancellationToken ct = default);
}

/// <summary>Creates <see cref="IEngineProcess"/> instances. Swapped for a fake in tests.</summary>
public interface IProcessLauncher
{
    IEngineProcess Create(string fileName, string arguments, string workingDirectory);
}

/// <summary>Default launcher: real child processes via <see cref="Process"/>.</summary>
public sealed class SystemProcessLauncher : IProcessLauncher
{
    public IEngineProcess Create(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        return new SystemProcess(new Process { StartInfo = psi, EnableRaisingEvents = true });
    }
}

/// <summary><see cref="IEngineProcess"/> backed by <see cref="System.Diagnostics.Process"/>.</summary>
internal sealed class SystemProcess : IEngineProcess
{
    private readonly Process _process;

    public SystemProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
        _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
    }

    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;

    public event EventHandler? Exited;
    public event Action<string>? OutputReceived;

    public bool Start()
    {
        if (!_process.Start())
            return false;
        // Tie the child to a kill-on-close job so it dies with the app even when the app is
        // force-killed (Task Manager / Stop-Process) — otherwise sing-box is orphaned, keeps the
        // TUN adapter, and the next connect fails with "Cannot create ... already exists".
        try { KillOnCloseJob.Assign(_process.Handle); } catch { /* best-effort */ }
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        return true;
    }

    public void Kill() => _process.Kill(entireProcessTree: true);

    public Task WaitForExitAsync(CancellationToken ct = default) => _process.WaitForExitAsync(ct);

    public void Dispose() => _process.Dispose();
}

/// <summary>
/// A single process-wide Windows job object with KILL_ON_JOB_CLOSE. Child processes assigned to
/// it are terminated when the last handle to the job closes — which, since only this process holds
/// it, happens when the app exits by ANY means (clean exit, crash, or Task Manager kill). This
/// guarantees sing-box never outlives the app and orphans the TUN adapter.
/// </summary>
internal static class KillOnCloseJob
{
    private static readonly nint _job = Create();

    public static void Assign(nint processHandle)
    {
        if (_job != nint.Zero && processHandle != nint.Zero)
            AssignProcessToJobObject(_job, processHandle);
    }

    private static nint Create()
    {
        try
        {
            var job = CreateJobObject(nint.Zero, null);
            if (job == nint.Zero) return nint.Zero;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
            };
            int len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            nint ptr = Marshal.AllocHGlobal(len);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)len))
                {
                    CloseHandle(job);
                    return nint.Zero;
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return job; // intentionally never closed: the OS closes it when the app dies -> kills children
        }
        catch { return nint.Zero; }
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount,
                     ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObject(nint lpJobAttributes, string? lpName);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint hJob, int infoClass, nint lpInfo, uint cbInfoLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}

/// <summary>Cleanup for a stale engine process that survived a previous run.</summary>
public static class EngineOrphans
{
    /// <summary>
    /// Kill any leftover engine process (matched by full exe path) that a previous run orphaned,
    /// so a fresh connect isn't blocked by it still holding the TUN adapter. Best-effort; requires
    /// the same or higher privilege than the orphan (the app is elevated when connecting).
    /// </summary>
    public static void KillStale(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000); // let it drop the TUN adapter before we recreate it
                }
            }
            catch { /* not ours / access denied / already gone */ }
            finally { p.Dispose(); }
        }
    }
}
