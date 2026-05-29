using System.IO;

namespace SimpleRay.App.Infrastructure;

/// <summary>Resolves where bundled binaries, geo data and user data live.</summary>
public static class AppPaths
{
    /// <summary>Folder next to the executable (holds core\ and geo\ when bundled).</summary>
    public static string BaseDir => AppContext.BaseDirectory;

    public static string CoreExe => Path.Combine(BaseDir, "core", "sing-box.exe");

    /// <summary>Directory containing geoip-*.srs / geosite-*.srs rule-sets.</summary>
    public static string GeoDir => Path.Combine(BaseDir, "geo");

    /// <summary>Per-user data: %AppData%\SimpleRay.</summary>
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleRay");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ProfilesFile => Path.Combine(DataDir, "profiles.json");

    /// <summary>Persisted routing settings (mode, geo presets, per-app rules).</summary>
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    /// <summary>Where the generated sing-box config is written at runtime.</summary>
    public static string RuntimeDir
    {
        get
        {
            var dir = Path.Combine(DataDir, "run");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
