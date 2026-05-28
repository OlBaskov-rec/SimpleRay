using System.Diagnostics;
using System.Text;

namespace SimpleRay.Core.Engine;

public enum EngineState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed class EngineOptions
{
    /// <summary>Full path to sing-box.exe.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Working dir where config.json is written and *.srs rule-sets live.</summary>
    public required string WorkingDirectory { get; init; }

    public string ConfigFileName { get; init; } = "config.json";
}

/// <summary>
/// Owns the sing-box child process: writes the generated config, optionally
/// validates it, starts/stops the process and surfaces its logs and state.
/// No control port is opened — the process is driven purely via config file
/// and OS process signals, preserving the no-loopback-proxy invariant.
/// </summary>
public sealed class EngineManager : IAsyncDisposable
{
    private readonly EngineOptions _options;
    private readonly object _gate = new();
    private Process? _process;

    public EngineManager(EngineOptions options) => _options = options;

    public EngineState State { get; private set; } = EngineState.Stopped;

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler<string>? LogReceived;

    public string ConfigPath => Path.Combine(_options.WorkingDirectory, _options.ConfigFileName);

    /// <summary>Runs `sing-box check` on the given config. Returns (ok, output).</summary>
    public async Task<(bool ok, string output)> CheckConfigAsync(string configJson, CancellationToken ct = default)
    {
        WriteConfig(configJson);
        var (exit, output) = await RunOnceAsync(
            $"check -c \"{ConfigPath}\" -D \"{_options.WorkingDirectory}\"", ct).ConfigureAwait(false);
        return (exit == 0, output);
    }

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
        process.Exited += OnProcessExited;

        lock (_gate)
        {
            _process = process;
        }

        if (!process.Start())
        {
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
            SetState(EngineState.Faulted);
            throw new InvalidOperationException(
                $"sing-box exited immediately (code {process.ExitCode}). Check logs / admin rights for TUN.");
        }

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
            if (!process.HasExited)
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
        // Only fires for unexpected exits; StopAsync detaches this handler first.
        SetState(EngineState.Faulted);
    }

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
        };

        using var process = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return (process.ExitCode, sb.ToString());
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
