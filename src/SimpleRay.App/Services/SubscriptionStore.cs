using System.IO;
using System.Text.Json;
using SimpleRay.App.Infrastructure;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Services;

/// <summary>A saved subscription: its URL, the provider's last metadata and when it was fetched.</summary>
public sealed class SubscriptionEntry
{
    public string Url { get; set; } = "";

    /// <summary>User-chosen User-Agent for fetching this subscription; null = the default.</summary>
    public string? UserAgent { get; set; }

    public SubscriptionMetadata Info { get; set; } = new();
    public DateTimeOffset? LastUpdated { get; set; }
}

/// <summary>Persists the user's subscriptions under %AppData%\SimpleRay.</summary>
public sealed class SubscriptionStore
{
    /// <summary>2: entries with metadata (1 was a bare URL list, still read).</summary>
    public const int CurrentSchema = 2;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed class Document
    {
        public int SchemaVersion { get; set; } = CurrentSchema;
        public List<SubscriptionEntry>? Items { get; set; }

        /// <summary>Schema 1 only.</summary>
        public List<string>? Urls { get; set; }
    }

    public List<SubscriptionEntry> Load()
    {
        var path = AppPaths.SubscriptionsFile;
        if (!File.Exists(path))
            return new List<SubscriptionEntry>();
        try
        {
            var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
            var items = doc?.Items
                        ?? doc?.Urls?.Select(u => new SubscriptionEntry { Url = u }).ToList()
                        ?? new List<SubscriptionEntry>();
            foreach (var e in items)
                e.Info ??= new SubscriptionMetadata();
            return Dedup(items.Where(e => !string.IsNullOrWhiteSpace(e.Url)));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<SubscriptionEntry>();
        }
    }

    public void Save(IEnumerable<SubscriptionEntry> entries)
    {
        var doc = new Document { Items = Dedup(entries) };
        AtomicFile.WriteAllText(AppPaths.SubscriptionsFile, JsonSerializer.Serialize(doc, Options));
    }

    private static List<SubscriptionEntry> Dedup(IEnumerable<SubscriptionEntry> entries) =>
        entries.GroupBy(e => e.Url).Select(g => g.First()).ToList();
}
