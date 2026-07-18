using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleRay.App.Infrastructure;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Services;

/// <summary>Persists <see cref="RoutingSettings"/> as JSON under %AppData%\SimpleRay.</summary>
public sealed class SettingsStore
{
    /// <summary>Bump when the on-disk settings shape changes; lets future loads migrate.</summary>
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class Document
    {
        public int SchemaVersion { get; set; } = CurrentSchema;
        public RoutingSettings Settings { get; set; } = new();
    }

    public RoutingSettings Load()
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path))
            return new RoutingSettings();
        try
        {
            var json = File.ReadAllText(path);
            using var probe = JsonDocument.Parse(json);
            // Current format is a versioned envelope; legacy was a bare RoutingSettings object.
            if (probe.RootElement.ValueKind == JsonValueKind.Object &&
                probe.RootElement.TryGetProperty(nameof(Document.SchemaVersion), out _))
            {
                return JsonSerializer.Deserialize<Document>(json, Options)?.Settings ?? new RoutingSettings();
            }
            return JsonSerializer.Deserialize<RoutingSettings>(json, Options) ?? new RoutingSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not prevent the app from starting.
            return new RoutingSettings();
        }
    }

    public void Save(RoutingSettings settings)
    {
        var doc = new Document { SchemaVersion = CurrentSchema, Settings = settings };
        AtomicFile.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(doc, Options));
    }
}
