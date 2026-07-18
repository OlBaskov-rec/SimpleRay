using System.IO;
using System.Text.Json;
using SimpleRay.App.Infrastructure;

namespace SimpleRay.App.Services;

/// <summary>Persists the user's subscription URLs under %AppData%\SimpleRay.</summary>
public sealed class SubscriptionStore
{
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed class Document
    {
        public int SchemaVersion { get; set; } = CurrentSchema;
        public List<string> Urls { get; set; } = new();
    }

    public List<string> Load()
    {
        var path = AppPaths.SubscriptionsFile;
        if (!File.Exists(path))
            return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options)?.Urls ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    public void Save(IEnumerable<string> urls)
    {
        var doc = new Document { Urls = urls.Distinct().ToList() };
        AtomicFile.WriteAllText(AppPaths.SubscriptionsFile, JsonSerializer.Serialize(doc, Options));
    }
}
