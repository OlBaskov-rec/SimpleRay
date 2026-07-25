using System.Diagnostics;
using System.Net.Sockets;

namespace SimpleRay.Core.Net;

/// <summary>
/// Measures TCP connect latency to a server directly (not through the tunnel) — a
/// reachability/quality hint for picking a server. Returns null on timeout or refusal.
/// </summary>
public static class LatencyProbe
{
    public static async Task<int?> MeasureAsync(string host, int port, int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535)
            return null;
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var sw = Stopwatch.StartNew();
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return null; // timeout, DNS failure, connection refused, etc.
        }
    }
}
