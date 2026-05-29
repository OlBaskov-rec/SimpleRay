using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleRay.App.Infrastructure;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Services;

/// <summary>Persists <see cref="RoutingSettings"/> as JSON under %AppData%\SimpleRay.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public RoutingSettings Load()
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path))
            return new RoutingSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RoutingSettings>(json, Options) ?? new RoutingSettings();
        }
        catch (JsonException)
        {
            return new RoutingSettings();
        }
    }

    public void Save(RoutingSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(AppPaths.SettingsFile, json);
    }
}
