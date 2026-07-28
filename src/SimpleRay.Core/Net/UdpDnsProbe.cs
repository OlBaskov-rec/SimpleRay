using System.Diagnostics;
using System.Net.Sockets;

namespace SimpleRay.Core.Net;

/// <summary>
/// Checks that a plain UDP DNS resolver (port 53) answers, and how fast, by validating a
/// real DNS reply rather than a bare socket send.
///
/// Inherent limit of UDP DNS: on a network with a transparent DNS proxy, every port-53
/// query is answered locally with the source address spoofed to the intended server, so
/// this probe (and DNS itself) cannot tell the real resolver from an interceptor — the
/// query would succeed for an unroutable address too. That is the unencrypted-transport
/// tradeoff, which is why UDP is offered only for direct traffic; <see cref="DohProbe"/>
/// is not affected, as an interceptor cannot forge the resolver's certificate.
/// Returns round-trip milliseconds, or null if nothing answered usefully.
/// </summary>
public static class UdpDnsProbe
{
    public static async Task<int?> MeasureAsync(string server, int timeoutMs = 4000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(server))
            return null;

        try
        {
            using var udp = new UdpClient(server, 53);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var query = DnsWire.BuildQuery();

            var sw = Stopwatch.StartNew();
            await udp.SendAsync(query, cts.Token).ConfigureAwait(false);
            var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();

            return DnsWire.IsSuccessfulResponse(result.Buffer) ? (int)sw.ElapsedMilliseconds : null;
        }
        catch
        {
            return null; // timeout, ICMP port-unreachable, no answer…
        }
    }
}
