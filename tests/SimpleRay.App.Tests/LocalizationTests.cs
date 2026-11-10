using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimpleRay.App.Localization;

namespace SimpleRay.App.Tests;

public class LocalizationTests
{
    private static readonly Assembly AppAsm = typeof(LocalizationManager).Assembly;

    private static Dictionary<string, string> Load(string code)
    {
        var name = AppAsm.GetManifestResourceNames()
            .First(n => n.EndsWith($".langs.{code}.json", StringComparison.OrdinalIgnoreCase));
        using var s = AppAsm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(r.ReadToEnd())!;
    }

    private static readonly string[] AllCodes = { "en", "ru", "fr", "de", "es", "fa", "zh", "uk", "tr" };

    public static IEnumerable<object[]> NonEnglishCodes =>
        AllCodes.Where(c => c != "en").Select(c => new object[] { c });

    [Fact]
    public void AllNineLanguages_AreEmbedded()
    {
        foreach (var code in AllCodes)
            Assert.NotEmpty(Load(code));
    }

    [Theory]
    [MemberData(nameof(NonEnglishCodes))]
    public void EveryLanguage_HasExactlyEnglishKeys(string code)
    {
        var en = Load("en");
        var other = Load(code);

        var missing = en.Keys.Except(other.Keys).ToList();
        var extra = other.Keys.Except(en.Keys).ToList();
        Assert.True(missing.Count == 0, $"{code}.json missing keys: {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0, $"{code}.json has extra keys: {string.Join(", ", extra)}");
    }

    [Theory]
    [MemberData(nameof(NonEnglishCodes))]
    public void EveryLanguage_HasNoEmptyValues(string code)
    {
        var empties = Load(code).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.True(empties.Count == 0, $"{code}.json has empty values: {string.Join(", ", empties)}");
    }

    // "lang:key" pairs whose non-English value legitimately equals English — product name,
    // unit, loanword, or cognate (adjudicated). Everything else that equals English is an
    // untranslated placeholder (e.g. left behind by scripts/loc-add-key.ps1).
    private static readonly HashSet<string> IdenticalToEnglishAllowed = new(StringComparer.Ordinal)
    {
        "ru:crash.title", "uk:crash.title", "de:crash.title", "fr:crash.title", "es:crash.title",
        "tr:crash.title", "zh:crash.title", "fa:crash.title",
        "de:routing.title", "de:routing.apps", "de:updates.check", "de:active.auto", "de:profile.ping", "de:dns.latency",
        "fr:routing.mode", "fr:active.auto", "fr:apps.toDirect", "fr:profile.ping", "fr:dns.latency",
        "es:status.error", "es:active.auto", "es:active.manual", "es:profile.ping", "es:dns.latency",
        "tr:profile.ping", "tr:dns.latency",
        // Unit: "Mbit/s" is written the same way in these languages.
        "de:speed.value", "fr:speed.value", "es:speed.value",
    };

    /// <summary>
    /// The set of non-English values equal to English must match the allowlist EXACTLY — so both
    /// a new untranslated placeholder (identical but not allowed) and a stale allowlist entry
    /// (allowed but since translated) fail, keeping the allowlist honest.
    /// </summary>
    [Theory]
    [MemberData(nameof(NonEnglishCodes))]
    public void IdenticalToEnglishValues_MatchAllowlistExactly(string code)
    {
        var en = Load("en");
        var other = Load(code);
        var actual = en.Keys
            .Where(k => other.TryGetValue(k, out var v) && v == en[k])
            .Select(k => $"{code}:{k}").ToHashSet();
        var expected = IdenticalToEnglishAllowed
            .Where(e => e.StartsWith($"{code}:", StringComparison.Ordinal)).ToHashSet();

        var untranslated = actual.Except(expected).ToList(); // == English but not allowed → translate it
        var stale = expected.Except(actual).ToList();         // allowed but now translated → drop from allowlist
        Assert.True(untranslated.Count == 0, $"{code}.json untranslated (== English): {string.Join(", ", untranslated)}");
        Assert.True(stale.Count == 0, $"{code}.json stale allowlist entries (now translated, remove them): {string.Join(", ", stale)}");
    }

    [Theory]
    [MemberData(nameof(NonEnglishCodes))]
    public void EveryLanguage_KeepsSamePlaceholders(string code)
    {
        var en = Load("en");
        var other = Load(code);
        foreach (var (key, enVal) in en)
        {
            var expected = Placeholders(enVal);
            var actual = Placeholders(other[key]);
            Assert.True(expected.SetEquals(actual),
                $"{code}.json[{key}] placeholders {{{string.Join(",", actual)}}} != en {{{string.Join(",", expected)}}}");
        }
    }

    private static HashSet<int> Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)\}").Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

    [Fact]
    public void Indexer_FallsBackToKey_ForUnknown()
    {
        Assert.Equal("no.such.key.exists", LocalizationManager.Instance["no.such.key.exists"]);
        Assert.NotEqual("connect.connect", LocalizationManager.Instance["connect.connect"]); // known → translated
    }

    /// <summary>The generated typed keys must mirror en.json exactly (run scripts/gen-lockeys.ps1 if this fails).</summary>
    [Fact]
    public void LocKeys_MirrorEnglishJsonExactly()
    {
        var jsonKeys = Load("en").Keys.ToHashSet();
        var constValues = typeof(LocKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        var missing = jsonKeys.Except(constValues).ToList(); // JSON keys without a const → regenerate
        var stale = constValues.Except(jsonKeys).ToList();    // consts for removed keys → regenerate
        Assert.True(missing.Count == 0, $"LocKeys missing consts (run gen-lockeys.ps1): {string.Join(", ", missing)}");
        Assert.True(stale.Count == 0, $"LocKeys has stale consts (run gen-lockeys.ps1): {string.Join(", ", stale)}");
    }

    /// <summary>Every key referenced from XAML via {loc:Loc ...} must exist in en.json (catches typos).</summary>
    [Fact]
    public void XamlLocKeys_ExistInEnglishJson()
    {
        var en = Load("en");
        var appDir = FindAppSourceDir();
        if (appDir is null) return; // source tree not present (e.g. packaged run) — skip

        var missing = new List<string>();
        foreach (var xaml in Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(xaml);
            foreach (Match m in Regex.Matches(text, @"loc:Loc\s+([A-Za-z0-9_.]+)"))
            {
                var key = m.Groups[1].Value;
                if (!en.ContainsKey(key))
                    missing.Add($"{Path.GetFileName(xaml)}: {key}");
            }
        }
        Assert.True(missing.Count == 0, "XAML {loc:Loc} keys not in en.json: " + string.Join(", ", missing));
    }

    private static string? FindAppSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SimpleRay.sln")))
            dir = dir.Parent;
        var app = dir is null ? null : Path.Combine(dir.FullName, "src", "SimpleRay.App");
        return app is not null && Directory.Exists(app) ? app : null;
    }
}
