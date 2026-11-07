using System.Net.Http;
using System.Text;
using SimpleRay.Core.Profiles;

namespace SimpleRay.App.Services;

/// <summary>
/// Fetches a subscription URL and parses it — body and response headers (profile-title,
/// subscription-userinfo, …) — into profiles tagged with their source.
/// </summary>
public sealed class SubscriptionService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Providers send raw UTF-8 in profile-title/announce; the default Latin-1 garbles it.
            ResponseHeaderEncodingSelector = (_, _) => Encoding.UTF8,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };
        var c = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 16 * 1024 * 1024,
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        return c;
    }

    public static string DefaultUserAgent => "SimpleRay/" + UpdateService.CurrentVersion.ToString(3);

    /// <summary>
    /// Cleans a user-entered User-Agent: trimmed, single line, at most 512 chars; null when empty
    /// (use the default). Line breaks are rejected outright so the value can't inject headers.
    /// </summary>
    public static string? NormalizeUserAgent(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            return null;
        return value.Length > 512 ? value[..512] : value;
    }

    /// <summary>
    /// Downloads and parses the subscription; each profile is tagged with <paramref name="url"/>.
    /// <paramref name="userAgent"/> replaces the default User-Agent for this request.
    /// </summary>
    public async Task<SubscriptionContent> FetchAsync(string url, string? userAgent = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // A request-level User-Agent wins over DefaultRequestHeaders. TryAddWithoutValidation
        // because real client UAs don't always follow the product/version grammar.
        if (NormalizeUserAgent(userAgent) is { } ua)
            request.Headers.TryAddWithoutValidation("User-Agent", ua);
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var headers = response.Headers.Concat(response.Content.Headers)
            .Select(h => KeyValuePair.Create(h.Key, string.Join(",", h.Value)));

        var content = SubscriptionParser.ParseContent(body, headers);
        content.Metadata.Title ??= SubscriptionLink.FragmentTitle(url);
        foreach (var p in content.Profiles)
            p.Subscription = url;
        return content;
    }
}
