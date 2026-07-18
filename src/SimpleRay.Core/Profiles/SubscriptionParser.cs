using System.Text;
using SimpleRay.Core.Models;

namespace SimpleRay.Core.Profiles;

/// <summary>
/// Parses a subscription response body into profiles. Providers usually return
/// base64 of newline-separated share links; some return the links in plain text.
/// Pure (no I/O) — the HTTP fetch lives in the app layer.
/// </summary>
public static class SubscriptionParser
{
    public static IReadOnlyList<ProfileConfig> Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<ProfileConfig>();

        var text = body.Trim();
        // If it isn't already a list of share links, try to base64-decode it.
        if (!text.Contains("://"))
        {
            var decoded = TryDecodeBase64(text);
            if (decoded is not null)
                text = decoded;
        }
        return ShareLinkParser.ParseMany(text);
    }

    private static string? TryDecodeBase64(string s)
    {
        s = s.Replace("\r", "").Replace("\n", "").Trim().Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
