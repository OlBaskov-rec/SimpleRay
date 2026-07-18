using System.Diagnostics;
using System.IO;
using System.Text;
using SimpleRay.Core.Engine;

namespace SimpleRay.App.Engine;

public sealed class EngineOptions
{
    /// <summary>Full path to sing-box.exe.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Working dir where config.json is written and *.srs rule-sets live.</summary>
    public required string WorkingDirectory { get; init; }

    public string ConfigFileName { get; init; } = "config.json";

    /// <summary>
    /// Optional graceful terminator. When set, <see cref="SingBoxEngine.StopAsync"/> asks it
    /// to stop the process cleanly (so sing-box can tear down the TUN adapter and routes)
    /// before falling back to a hard kill.
    /// </summary>
    public IProcessTerminator? Terminator { get; init; }

    /// <summary>How long to wait for a graceful stop before hard-killing.</summary>
    public TimeSpan GracefulStopTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Strategy for stopping the child process cleanly (e.g. sending CTRL+C on Windows).
/// Implementations must never throw — returning false lets the caller hard-kill instead.
/// </summary>
public interface IProcessTerminator
{
    /// <summary>Attempt a graceful stop. Returns true only if the process exited within <paramref name="timeout"/>.</summary>
    Task<bool> TryGracefulStopAsync(Process process, TimeSpan timeout);
}

/// <summary>
/// Windows <see cref="IVpnEngine"/>: owns the sing-box child process — writes the
/// generated config, starts/stops the process and surfaces its logs and state.
/// No control port is opened — the process is driven purely via config file and OS
/// process signals, preserving the no-loopback-proxy invariant.
/// </summary>
public sealed class SingBoxEngine : IVpnEngine
{
    private readonly EngineOptions _options;
    private readonly object _gate = new();
    private Process? _process;

    public SingBoxEngine(EngineOptions options) => _options = options;

    public EngineState State { get; private set; } = EngineState.Stopped;

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler<string>? LogReceived;

    public string ConfigPath => Path.Combine(_options.WorkingDirectory, _options.ConfigFileName);

    public async Task StartAsync(string configJson, CancellationToken ct = default)
    {
        EnsureFilesExist();
        if (State is EngineState.Running or EngineState.Starting)
            throw new InvalidOperationException("Engine already running.");

        WriteConfig(configJson);
        SetState(EngineState.Starting);

        var psi = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            Arguments = $"run -c \"{ConfigPath}\" -D \"{_options.WorkingDirectory}\"",
            WorkingDirectory = _options.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) LogReceived?.Invoke(this, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) LogReceived?.Invoke(this, e.Data); };
        // The crash handler is attached only once we know the process is up, so the
        // fail-fast probe below can read HasExited/ExitCode without racing a disposal.

        lock (_gate)
        {
            _process = process;
        }

        if (!process.Start())
        {
            ClearProcess(process);
            SetState(EngineState.Faulted);
            throw new InvalidOperationException("Failed to start sing-box.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Give the process a moment to fail fast (bad config, no admin for TUN).
        try
        {
            await Task.Delay(700, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }

        if (process.HasExited)
        {
            int code = process.ExitCode;
            ClearProcess(process);
            SetState(EngineState.Faulted);
            throw new InvalidOperationException(
                $"sing-box exited immediately (code {code}). Check logs / admin rights for TUN.");
        }

        process.Exited += OnProcessExited;
        SetState(EngineState.Running);
    }

    public async Task StopAsync()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }
        if (process is null)
        {
            SetState(EngineState.Stopped);
            return;
        }

        SetState(EngineState.Stopping);
        process.Exited -= OnProcessExited;
        try
        {
            // 1) Try a graceful stop so sing-box removes the TUN adapter/routes itself.
            bool exited = false;
            if (_options.Terminator is { } terminator && !process.HasExited)
            {
                try
                {
                    exited = await terminator.TryGracefulStopAsync(process, _options.GracefulStopTimeout)
                        .ConfigureAwait(false);
                }
                catch
                {
                    exited = false; // never let a terminator failure block shutdown
                }
            }

            // 2) Hard-kill fallback if it didn't exit cleanly.
            if (!process.HasExited && !exited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException) { /* already gone */ }
        finally
        {
            process.Dispose();
            SetState(EngineState.Stopped);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        // Fires only for unexpected exits; StopAsync detaches this handler first.
        if (sender is Process p)
            ClearProcess(p);
        SetState(EngineState.Faulted);
    }

    /// <summary>Detaches, clears and disposes the process if it is the current one.</summary>
    private void ClearProcess(Process process)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_process, process))
                _process = null;
        }
        try { process.Exited -= OnProcessExited; } catch { /* ignore */ }
        try { process.Dispose(); } catch { /* ignore */ }
    }

    private void WriteConfig(string configJson)
    {
        Directory.CreateDirectory(_options.WorkingDirectory);
        File.WriteAllText(ConfigPath, configJson, new UTF8Encoding(false));
    }

    private void EnsureFilesExist()
    {
        if (!File.Exists(_options.ExecutablePath))
            throw new FileNotFoundException("sing-box executable not found.", _options.ExecutablePath);
    }

    private void SetState(EngineState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
