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
    /// Run <c>sing-box check</c> on the generated config before starting, so a config
    /// error surfaces as a real diagnostic instead of the opaque "exited immediately".
    /// </summary>
    public bool ValidateBeforeStart { get; init; } = true;

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

        // Pre-flight validation, before we enter Starting or spawn the tunnel: a bad
        // config throws here with the checker's own message, so the caller shows a real
        // reason instead of the later "exited immediately (code 1)".
        if (_options.ValidateBeforeStart)
            await ValidateConfigAsync(ct).ConfigureAwait(false);

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

    /// <summary>Runs <c>sing-box check</c>; returns (ok, combined output). No side effects.</summary>
    public async Task<(bool ok, string output)> CheckConfigAsync(string configJson, CancellationToken ct = default)
    {
        EnsureFilesExist();
        WriteConfig(configJson);
        var (exit, output) = await RunOnceAsync(
            $"check -c \"{ConfigPath}\" -D \"{_options.WorkingDirectory}\"", ct).ConfigureAwait(false);
        return (exit == 0, output);
    }

    /// <summary>Validates the already-written config; throws with diagnostics if invalid.</summary>
    private async Task ValidateConfigAsync(CancellationToken ct)
    {
        var (exit, output) = await RunOnceAsync(
            $"check -c \"{ConfigPath}\" -D \"{_options.WorkingDirectory}\"", ct).ConfigureAwait(false);
        if (exit == 0)
            return;

        // The config holds secrets; don't leave the rejected one on disk.
        TryDeleteConfig();
        // Full checker output to the log, a concise reason on the exception (which the UI shows).
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            LogReceived?.Invoke(this, line.TrimEnd());
        throw new InvalidOperationException("Invalid configuration: " + ExtractReason(output));
    }

    /// <summary>Picks the most telling line of a checker failure for a one-line message.</summary>
    private static string ExtractReason(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return "sing-box check failed.";
        var flagged = lines.LastOrDefault(l =>
            l.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("ERROR", StringComparison.OrdinalIgnoreCase));
        return flagged ?? lines[^1];
    }

    /// <summary>Runs sing-box once to completion, capturing stdout+stderr combined.</summary>
    private async Task<(int exitCode, string output)> RunOnceAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = _options.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        // stdout and stderr callbacks fire on different threads — serialize appends.
        void Append(string? data) { if (data is not null) lock (sb) sb.AppendLine(data); }
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        lock (sb) return (process.ExitCode, sb.ToString());
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
            TryDeleteConfig(); // the generated config holds secrets — don't leave it on disk
            SetState(EngineState.Stopped);
        }
    }

    private void TryDeleteConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
                File.Delete(ConfigPath);
        }
        catch { /* best-effort cleanup */ }
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
