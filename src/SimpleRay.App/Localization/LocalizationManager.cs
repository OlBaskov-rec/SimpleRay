using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using SimpleRay.App.Infrastructure;

namespace SimpleRay.App.Localization;

public sealed record LanguageOption(string Code, string NativeName);

/// <summary>
/// Runtime-switchable localization. Bindings use the string indexer (<c>this[key]</c>);
/// <see cref="SetCulture"/> raises the "Item[]" change so every bound element re-reads
/// its string live. Strings are per-language JSON embedded resources; missing keys fall
/// back to English, then to the key itself.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LanguageOption> Languages { get; } = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("ru", "Русский"),
        new LanguageOption("fr", "Français"),
        new LanguageOption("de", "Deutsch"),
        new LanguageOption("es", "Español"),
        new LanguageOption("fa", "فارسی"),
        new LanguageOption("zh", "中文"),
        new LanguageOption("uk", "Українська"),
        new LanguageOption("tr", "Türkçe"),
    };

    private Dictionary<string, string> _current = new();
    private readonly Dictionary<string, string> _fallback;

    private LocalizationManager()
    {
        _fallback = Load("en");
        _currentCode = "en";
        SetCulture(ResolveInitialCulture(), persist: false);
    }

    private string _currentCode;
    public string CurrentCulture => _currentCode;

    public FlowDirection FlowDirection =>
        _currentCode == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Localized string for <paramref name="key"/> (fallback: English, then the key).</summary>
    public string this[string key] =>
        _current.TryGetValue(key, out var v) ? v
        : _fallback.TryGetValue(key, out var f) ? f
        : key;

    /// <summary>Formats a localized template with args (uses <see cref="this[string]"/>).</summary>
    public string Format(string key, params object[] args) =>
        string.Format(this[key], args);

    public void SetCulture(string code, bool persist = true)
    {
        _current = Load(code);
        _currentCode = code;

        var ci = SafeCulture(code);
        CultureInfo.CurrentUICulture = ci;
        CultureInfo.CurrentCulture = ci;

        if (persist) SaveCulture(code);

        // Refresh every binding to the indexer, plus culture-dependent props.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after the culture changes so view models can rebuild non-bound labels.</summary>
    public event EventHandler? CultureChanged;

    private static Dictionary<string, string> Load(string code)
    {
        try
        {
            var asm = typeof(LocalizationManager).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($".{code}.json", StringComparison.OrdinalIgnoreCase));
            if (name is null) return new();
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private string ResolveInitialCulture()
    {
        var saved = TryLoadSavedCulture();
        if (saved is not null && Languages.Any(l => l.Code == saved))
            return saved;
        var sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == sys) ? sys : "en";
    }

    private static CultureInfo SafeCulture(string code)
    {
        try { return CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    private static string LangFile => Path.Combine(AppPaths.DataDir, "lang.txt");

    private static string? TryLoadSavedCulture()
    {
        try { return File.Exists(LangFile) ? File.ReadAllText(LangFile).Trim() : null; }
        catch { return null; }
    }

    private static void SaveCulture(string code)
    {
        try { File.WriteAllText(LangFile, code); }
        catch { /* best-effort */ }
    }
}
