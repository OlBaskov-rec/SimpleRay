namespace SimpleRay.Core.Profiles;

public enum SubscriptionLinkStatus
{
    Ok,
    Invalid,
    /// <summary>happ://crypt…/ — the URL is encrypted with Happ's private key; only Happ can open it.</summary>
    HappEncrypted,
}

/// <summary>
/// Turns what the user pasted into a fetchable subscription URL. Accepts a plain http(s) URL,
/// the Happ deep link <c>happ://add/&lt;url&gt;</c>, and the import links of other clients that
/// carry the URL in the clear: a <c>url=</c> query parameter (<c>throne://subscribe?url=…</c>,
/// <c>sing-box://import-remote-profile?url=…</c>, <c>clash://install-config?url=…</c>) or an
/// <c>import/</c> / <c>add/</c> path (<c>hiddify://import/…</c>, <c>v2raytun://import/…</c>). The
/// embedded URL may be raw, percent-encoded or base64. Encrypted Happ links
/// (<c>happ://crypt…</c>) are recognised and reported, not decrypted.
/// </summary>
public static class SubscriptionLink
{
    private const string HappScheme = "happ://";
    private static readonly string[] PathPrefixes = { "add/", "import/", "install-sub/", "install-config/" };

    public static SubscriptionLinkStatus Normalize(string? input, out string url)
    {
        url = "";
        var s = (input ?? "").Trim();
        if (s.StartsWith(HappScheme, StringComparison.OrdinalIgnoreCase) &&
            s[HappScheme.Length..].StartsWith("crypt", StringComparison.OrdinalIgnoreCase))
            return SubscriptionLinkStatus.HappEncrypted;

        if (!IsHttpUrl(s))
        {
            int sep = s.IndexOf("://", StringComparison.Ordinal);
            if (sep <= 0 || ExtractEmbedded(s[(sep + 3)..]) is not { } embedded)
                return SubscriptionLinkStatus.Invalid;
            s = embedded;
        }

        url = s;
        return SubscriptionLinkStatus.Ok;
    }

    // The part after "<scheme>://": "add/<url>", "import/<url>", or "<action>?…url=<url>…".
    private static string? ExtractEmbedded(string rest)
    {
        foreach (var prefix in PathPrefixes)
        {
            if (rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return DecodePayload(rest[prefix.Length..]);
        }

        int q = rest.IndexOf('?');
        if (q < 0)
            return null;
        foreach (var pair in rest[(q + 1)..].Split('&'))
        {
            if (!pair.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
                continue;
            // A raw (unencoded) URL may itself contain '&' and '#': take everything after "url=".
            var value = pair["url=".Length..];
            if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !value.Contains('%'))
                value = rest[(rest.IndexOf(pair, q, StringComparison.Ordinal) + "url=".Length)..];
            return DecodePayload(value);
        }
        return null;
    }

    /// <summary>
    /// Title from a Happ-style fragment (<c>https://host/sub#My_VPN?fragment=…</c>), used when the
    /// provider sends no profile-title. The fragment is never sent to the server.
    /// </summary>
    public static string? FragmentTitle(string url)
    {
        int hash = url.IndexOf('#');
        if (hash < 0)
            return null;
        var frag = url[(hash + 1)..];
        int q = frag.IndexOf('?');
        if (q >= 0)
            frag = frag[..q];
        try
        {
            frag = Uri.UnescapeDataString(frag).Trim();
        }
        catch (UriFormatException)
        {
            return null;
        }
        return frag.Length > 0 ? frag : null;
    }

    /// <summary>The embedded URL as-is, percent-decoded or base64-decoded; null if none is an http(s) URL.</summary>
    private static string? DecodePayload(string payload)
    {
        payload = payload.Trim();
        if (IsHttpUrl(payload))
            return payload;
        var unescaped = Uri.UnescapeDataString(payload);
        if (IsHttpUrl(unescaped))
            return unescaped;
        var decoded = SubscriptionParser.TryDecodeBase64(payload)?.Trim();
        return decoded is not null && IsHttpUrl(decoded) ? decoded : null;
    }

    private static bool IsHttpUrl(string s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        !string.IsNullOrEmpty(uri.Host);
}
