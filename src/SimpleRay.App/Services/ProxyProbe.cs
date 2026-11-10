using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using SimpleRay.App.Engine;
using SimpleRay.App.Infrastructure;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Services;

/// <summary>
/// "Ping through the server": measures how long a real request takes THROUGH each server — the
/// same check the auto-switch (urltest) uses — without connecting. Starts a throwaway sing-box
/// with one local SOCKS inbound per server (127.0.0.1, random port, random username/password),
/// requests the test URL through each inbound in parallel, then kills sing-box and deletes its
/// config. Nothing system-wide changes: no TUN, no routes, the main connection is untouched.
/// </summary>
public sealed class ProxyProbe
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly IProcessLauncher _launcher;
    private readonly string _coreExe;
    private readonly string _workDir;

    public ProxyProbe(IProcessLauncher? launcher = null, string? coreExe = null, string? workDir = null)
    {
        _launcher = launcher ?? new SystemProcessLauncher();
        _coreExe = coreExe ?? AppPaths.CoreExe;
        _workDir = workDir ?? Path.Combine(AppPaths.RuntimeDir, "probe");
    }

    /// <summary>Latency in ms per profile (same order), or null where the request failed.</summary>
    public async Task<IReadOnlyList<int?>> MeasureAsync(IReadOnlyList<ProfileConfig> profiles, string testUrl, CancellationToken ct = default)
    {
        var results = new int?[profiles.Count];
        if (profiles.Count == 0)
            return results;
        if (await TryRunAsync(profiles, testUrl, results, ct).ConfigureAwait(false) || profiles.Count == 1)
            return results;

        // sing-box refuses to start when any one outbound is invalid; retry one by one so the
        // other servers still get a result and only the broken one shows a failure.
        for (int i = 0; i < profiles.Count; i++)
        {
            var one = new int?[1];
            if (await TryRunAsync(new[] { profiles[i] }, testUrl, one, ct).ConfigureAwait(false))
                results[i] = one[0];
        }
        return results;
    }

    /// <summary>One sing-box for all <paramref name="profiles"/>; false when it didn't come up.</summary>
    private async Task<bool> TryRunAsync(IReadOnlyList<ProfileConfig> profiles, string testUrl, int?[] results, CancellationToken ct)
    {
        Directory.CreateDirectory(_workDir);
        var ports = FreeLoopbackPorts(profiles.Count);
        var user = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var pass = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        // The config holds server credentials: unique name, deleted as soon as we're done.
        var configPath = Path.Combine(_workDir, $"probe-{Guid.NewGuid():N}.json");
        IEngineProcess? process = null;
        try
        {
            File.WriteAllText(configPath, SingBoxConfigGenerator.GenerateProbeJson(profiles, ports, user, pass));
            process = _launcher.Create(_coreExe, $"run -c \"{configPath}\" -D \"{_workDir}\"", _workDir);
            if (!process.Start() || !await WaitForPortAsync(ports[^1], process, ct).ConfigureAwait(false))
                return false;

            await Task.WhenAll(Enumerable.Range(0, profiles.Count).Select(async i =>
                results[i] = await MeasureOneAsync(ports[i], user, pass, testUrl, ct).ConfigureAwait(false)))
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            try { if (process is { HasExited: false }) process.Kill(); } catch (InvalidOperationException) { }
            process?.Dispose();
            try { File.Delete(configPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>One request through the local inbound; ms from connect to response headers.</summary>
    private static async Task<int?> MeasureOneAsync(int port, string user, string pass, string testUrl, CancellationToken ct)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                // SOCKS5 with username/password: no 407 challenge round-trip skewing the timing.
                Proxy = new WebProxy($"socks5://127.0.0.1:{port}") { Credentials = new NetworkCredential(user, pass) },
                UseProxy = true,
                AllowAutoRedirect = false,
            };
            using var http = new HttpClient(handler) { Timeout = RequestTimeout };
            var sw = Stopwatch.StartNew();
            using var response = await http.GetAsync(testUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            sw.Stop();
            // generate_204 answers 204; any non-error answer proves the path works.
            return (int)response.StatusCode is >= 200 and < 400 ? (int)sw.ElapsedMilliseconds : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (ct.IsCancellationRequested) throw;
            return null;
        }
    }

    /// <summary>Waits until sing-box listens (its inbounds start together) or exits.</summary>
    private static async Task<bool> WaitForPortAsync(int port, IEngineProcess process, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                return false;
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port, ct).ConfigureAwait(false);
                return true;
            }
            catch (SocketException)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
        return false;
    }

    /// <summary>Distinct free loopback ports (all held open together so none repeats).</summary>
    internal static int[] FreeLoopbackPorts(int count)
    {
        var listeners = new List<TcpListener>(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                listeners.Add(l);
            }
            return listeners.Select(l => ((IPEndPoint)l.LocalEndpoint).Port).ToArray();
        }
        finally
        {
            foreach (var l in listeners) l.Stop();
        }
    }
}
