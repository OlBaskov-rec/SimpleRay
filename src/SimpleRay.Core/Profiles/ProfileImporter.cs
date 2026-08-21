using SimpleRay.Core.Models;

namespace SimpleRay.Core.Profiles;

/// <summary>Result of an import: the parsed profiles not already present, and whether anything parsed at all.</summary>
public sealed record ProfileImportResult(IReadOnlyList<ProfileConfig> New, bool AnyParsed);

/// <summary>
/// Pure parse+dedup step of profile import, split out of the view-model so it is testable and
/// reusable. Deduplicates by <see cref="ProfileConfig.Raw"/> against the existing profiles AND
/// within the pasted batch (two identical links add one), matching the original inline behaviour.
/// </summary>
public static class ProfileImporter
{
    public static ProfileImportResult Import(string text, IEnumerable<ProfileConfig> existing)
    {
        var parsed = ShareLinkParser.ParseMany(text);
        if (parsed.Count == 0)
            return new ProfileImportResult(Array.Empty<ProfileConfig>(), AnyParsed: false);

        var seen = new HashSet<string?>(existing.Select(p => p.Raw));
        var fresh = new List<ProfileConfig>();
        foreach (var p in parsed)
            if (seen.Add(p.Raw)) // false when already present (existing or earlier in this batch)
                fresh.Add(p);

        return new ProfileImportResult(fresh, AnyParsed: true);
    }
}
