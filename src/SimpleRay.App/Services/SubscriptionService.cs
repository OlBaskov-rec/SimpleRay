using System.Net.Http;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;

namespace SimpleRay.App.Services;

/// <summary>Fetches a subscription URL and parses it into profiles tagged with their source.</summary>
public sealed class SubscriptionService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SimpleRay");
        return c;
    }

    /// <summary>Downloads and parses the subscription; each profile is tagged with <paramref name="url"/>.</summary>
    public async Task<IReadOnlyList<ProfileConfig>> FetchAsync(string url, CancellationToken ct = default)
    {
        var body = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        var profiles = SubscriptionParser.Parse(body);
        foreach (var p in profiles)
            p.Subscription = url;
        return profiles;
    }
}
