using System.IO;

namespace SimpleRay.App.Infrastructure;

/// <summary>
/// Crash-safe file writes: the content is written to a temp sibling first and then
/// atomically swapped in, so a crash or power loss mid-write can never leave a
/// half-written (corrupt) settings/profiles file behind.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        if (File.Exists(path))
            File.Replace(tmp, path, destinationBackupFileName: null);
        else
            File.Move(tmp, path);
    }
}
