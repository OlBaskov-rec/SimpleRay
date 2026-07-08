using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleRay.App.Infrastructure;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Services;

/// <summary>Persists the user's profile list as JSON under %AppData%\SimpleRay.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public List<ProfileConfig> Load()
    {
        var path = AppPaths.ProfilesFile;
        if (!File.Exists(path))
            return new List<ProfileConfig>();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ProfileConfig>>(json, Options) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not prevent the app from starting.
            return new List<ProfileConfig>();
        }
    }

    public void Save(IEnumerable<ProfileConfig> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, Options);
        AtomicFile.WriteAllText(AppPaths.ProfilesFile, json);
    }
}
