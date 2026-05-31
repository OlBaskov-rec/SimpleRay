using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleRay.Core.Update;

/// <summary>
/// Pure parsing of a GitHub "latest release" JSON: picks the win-x64 zip + its
/// .sha256 sidecar and decides whether the release is newer than the current
/// version. No I/O — the HTTP call lives in the app layer.
/// </summary>
public static class ReleaseParser
{
    private const string ZipSuffix = "win-x64.zip";

    /// <summary>
    /// Returns true and sets <paramref name="info"/> when <paramref name="json"/> describes a
    /// release newer than <paramref name="current"/> that has both the win-x64 zip and its .sha256.
    /// </summary>
    public static bool TryParseLatest(string json, Version current, out ReleaseInfo? info)
    {
        info = null;
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return false; }
        if (root is null) return false;

        var tag = (string?)root["tag_name"];
        if (string.IsNullOrWhiteSpace(tag)) return false;
        if (!TryParseVersion(tag, out var version)) return false;
        if (version.CompareTo(Normalize(current)) <= 0) return false;

        if (root["assets"] is not JsonArray assets) return false;

        string? zipUrl = null, shaUrl = null;
        foreach (var a in assets.OfType<JsonObject>())
        {
            var name = (string?)a["name"];
            var url = (string?)a["browser_download_url"];
            if (name is null || url is null) continue;

            if (name.EndsWith(ZipSuffix + ".sha256", StringComparison.OrdinalIgnoreCase))
                shaUrl = url;
            else if (name.EndsWith(ZipSuffix, StringComparison.OrdinalIgnoreCase))
                zipUrl = url;
        }
        if (zipUrl is null || shaUrl is null) return false;

        info = new ReleaseInfo
        {
            Version = version,
            ZipUrl = zipUrl,
            Sha256Url = shaUrl,
            Notes = (string?)root["body"],
            HtmlUrl = (string?)root["html_url"],
        };
        return true;
    }

    /// <summary>Parses "v1.2.3" / "1.2" / "1.2.3.4" into a 4-component <see cref="Version"/>.</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var s = tag.Trim().TrimStart('v', 'V');
        // Drop any pre-release/build suffix (e.g. "1.2.3-beta").
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];

        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var nums = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (i < parts.Length)
            {
                if (!int.TryParse(parts[i], out nums[i]) || nums[i] < 0) return false;
            }
            else nums[i] = 0;
        }
        version = new Version(nums[0], nums[1], nums[2], nums[3]);
        return true;
    }

    /// <summary>Pads a Version's unspecified components to 0 so comparisons are stable.</summary>
    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build, v.Revision < 0 ? 0 : v.Revision);
}
