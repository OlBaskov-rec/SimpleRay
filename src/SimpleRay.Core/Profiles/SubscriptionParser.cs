using System.Globalization;
using System.Text;
using SimpleRay.Core.Models;

namespace SimpleRay.Core.Profiles;

/// <summary>
/// Parsed subscription: the profiles plus the provider's metadata. <see cref="Skipped"/> counts
/// servers that were recognised but can't be used (e.g. an Xray transport sing-box lacks);
/// <see cref="Encrypted"/> is set when the body is Happ AES-encrypted content we can't read.
/// </summary>
public sealed record SubscriptionContent(
    IReadOnlyList<ProfileConfig> Profiles,
    SubscriptionMetadata Metadata,
    int Skipped = 0,
    bool Encrypted = false);

/// <summary>
/// Parses a subscription response into profiles and metadata. Body formats: base64 of
/// newline-separated share links, the same links in plain text, or a JSON array of full Xray
/// configs (what Happ-oriented panels such as Remnawave serve). Metadata comes from the
/// response headers and/or <c>#key: value</c> lines at the top of the body; a header wins over
/// the same key in the body. Pure (no I/O) — the HTTP fetch lives in the app layer.
/// </summary>
public static class SubscriptionParser
{
    /// <summary>Profiles only — kept for callers that don't need the metadata.</summary>
    public static IReadOnlyList<ProfileConfig> Parse(string body) => ParseContent(body).Profiles;

    public static SubscriptionContent ParseContent(
        string body, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        var meta = new SubscriptionMetadata();
        bool encrypted = false;
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                if (name.Equals("encrypt-tag", StringComparison.OrdinalIgnoreCase))
                    encrypted = true;
                else
                    ApplyField(meta, name, value, overwrite: true);
            }
        }
        if (encrypted)
            return new SubscriptionContent(Array.Empty<ProfileConfig>(), meta, Encrypted: true);

        var text = (body ?? "").Trim().TrimStart('﻿');
        if (text.Length == 0)
            return new SubscriptionContent(Array.Empty<ProfileConfig>(), meta);

        // If it isn't already a list of share links / metadata lines / JSON, try base64.
        if (!text.Contains("://") && !text.StartsWith('#') && !XrayJsonParser.LooksLikeJson(text))
        {
            var decoded = TryDecodeBase64(text);
            if (decoded is not null)
                text = decoded.Trim().TrimStart('﻿');
        }

        if (XrayJsonParser.LooksLikeJson(text))
        {
            var json = XrayJsonParser.Parse(text);
            return new SubscriptionContent(json.Profiles, meta, json.Skipped);
        }

        var links = new StringBuilder();
        foreach (var rawLine in text.Replace("\r", "\n").Split('\n'))
        {
            if (!TryApplyBodyField(meta, rawLine.Trim()))
                links.Append(rawLine).Append('\n');
        }
        return new SubscriptionContent(ShareLinkParser.ParseMany(links.ToString()), meta);
    }

    // "#profile-title: Name" (or "#providerid 123"). Only known keys are consumed, so the
    // comments of an embedded WireGuard .conf stay intact. Body values never overwrite headers.
    private static bool TryApplyBodyField(SubscriptionMetadata meta, string line)
    {
        if (!line.StartsWith('#'))
            return false;
        int colon = line.IndexOf(':');
        if (colon < 0)
            return false;
        var name = line[1..colon].Trim();
        return ApplyField(meta, name, line[(colon + 1)..].Trim(), overwrite: false);
    }

    /// <summary>Applies one metadata field; returns false for keys we don't know.</summary>
    private static bool ApplyField(SubscriptionMetadata meta, string name, string value, bool overwrite)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "profile-title":
                if (overwrite || meta.Title is null) meta.Title = NullIfEmpty(DecodeValue(value));
                return true;
            case "subscription-userinfo":
                if (overwrite || (meta.Used is null && meta.Total is null && meta.Expire is null))
                    ApplyUserInfo(meta, value);
                return true;
            case "profile-update-interval":
                if ((overwrite || meta.UpdateIntervalHours is null) &&
                    int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) &&
                    hours > 0)
                    meta.UpdateIntervalHours = hours;
                return true;
            case "support-url":
                if (overwrite || meta.SupportUrl is null) meta.SupportUrl = HttpUrlOrNull(value);
                return true;
            case "profile-web-page-url":
                if (overwrite || meta.WebPageUrl is null) meta.WebPageUrl = HttpUrlOrNull(value);
                return true;
            case "announce":
                if (overwrite || meta.Announce is null) meta.Announce = NullIfEmpty(DecodeValue(value));
                return true;
            default:
                return false;
        }
    }

    // "upload=0; download=2153701362; total=0; expire=1790951622" — bytes and Unix seconds;
    // total=0 means unlimited, expire=0 means never.
    private static void ApplyUserInfo(SubscriptionMetadata meta, string value)
    {
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            // Some panels send floats ("1.5e9"); round down to whole bytes/seconds.
            if (!double.TryParse(part[(eq + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
                d < 0 || d > long.MaxValue)
                continue;
            long n = (long)d;
            switch (part[..eq].Trim().ToLowerInvariant())
            {
                case "upload": meta.Upload = n; break;
                case "download": meta.Download = n; break;
                case "total": meta.Total = n > 0 ? n : null; break;
                case "expire":
                    meta.Expire = n > 0 && n <= 253402300799 // DateTimeOffset max (year 9999)
                        ? DateTimeOffset.FromUnixTimeSeconds(n)
                        : null;
                    break;
            }
        }
    }

    // Values may be sent as "base64:<b64 of UTF-8>" so non-ASCII survives HTTP headers.
    private static string DecodeValue(string value)
    {
        value = value.Trim();
        if (value.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            return TryDecodeBase64(value["base64:".Length..])?.Trim() ?? "";
        return value;
    }

    private static string? HttpUrlOrNull(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.ToString()
            : null;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    internal static string? TryDecodeBase64(string s)
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
