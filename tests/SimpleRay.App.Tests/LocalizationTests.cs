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
}
