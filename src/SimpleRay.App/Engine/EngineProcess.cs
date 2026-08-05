using System.Diagnostics;
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
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        return true;
    }

    public void Kill() => _process.Kill(entireProcessTree: true);

    public Task WaitForExitAsync(CancellationToken ct = default) => _process.WaitForExitAsync(ct);

    public void Dispose() => _process.Dispose();
}
