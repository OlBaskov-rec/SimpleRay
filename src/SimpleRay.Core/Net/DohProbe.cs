using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace SimpleRay.Core.Net;

/// <summary>
/// Checks that a DNS-over-HTTPS resolver actually answers on its IP, and how fast.
/// Sends a real RFC 8484 query rather than just opening a socket, because the ways a
/// DoH resolver fails in practice — no certificate valid for the bare IP, HTTP/1.1-only,
/// a captive portal answering 200 with junk — all pass a plain TCP connect.
/// Returns round-trip milliseconds, or null if the resolver is unusable.
/// </summary>
public static class DohProbe
{
    private static readonly HttpClient Http = new();

    public static async Task<int?> MeasureAsync(string server, int timeoutMs = 4000, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(server))
            return null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            using var request = BuildRequest(server);

            var sw = Stopwatch.StartNew();
            using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            var body = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();

            return DnsWire.IsSuccessfulResponse(body) ? (int)sw.ElapsedMilliseconds : null;
        }
        catch
        {
            return null; // TLS failure, timeout, HTTP/2 unsupported, DNS refusal…
        }
    }

    /// <summary>
    /// Builds the RFC 8484 GET request. The HTTP version must be set on the request
    /// message itself: HttpClient.DefaultRequestVersion is ignored for a manually
    /// constructed HttpRequestMessage, so relying on it silently sends HTTP/1.1 — which
    /// HTTP/2-only resolvers (e.g. Quad9) answer with 505, looking like an outage.
    /// </summary>
    internal static HttpRequestMessage BuildRequest(string server)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://{server}/dns-query?dns={Base64Url(DnsWire.BuildQuery())}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        request.Headers.Accept.ParseAdd("application/dns-message");
        return request;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
