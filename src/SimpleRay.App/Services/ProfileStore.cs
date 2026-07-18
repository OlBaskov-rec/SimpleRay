using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleRay.App.Infrastructure;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Services;

/// <summary>Persists the user's profile list as JSON under %AppData%\SimpleRay.</summary>
public sealed class ProfileStore
{
    /// <summary>Bump when the on-disk profile shape changes; lets future loads migrate.</summary>
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
        public List<ProfileConfig> Profiles { get; set; } = new();
    }

    public List<ProfileConfig> Load()
    {
        var path = AppPaths.ProfilesFile;
        if (!File.Exists(path))
            return new List<ProfileConfig>();
        try
        {
            var json = File.ReadAllText(path);
            // Legacy (unversioned) format was a bare array; current format is a versioned envelope.
            if (json.TrimStart().StartsWith('['))
                return JsonSerializer.Deserialize<List<ProfileConfig>>(json, Options) ?? new();
            return JsonSerializer.Deserialize<Document>(json, Options)?.Profiles ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not prevent the app from starting.
            return new List<ProfileConfig>();
        }
    }

    public void Save(IEnumerable<ProfileConfig> profiles)
    {
        var doc = new Document { SchemaVersion = CurrentSchema, Profiles = profiles.ToList() };
        AtomicFile.WriteAllText(AppPaths.ProfilesFile, JsonSerializer.Serialize(doc, Options));
    }
}
