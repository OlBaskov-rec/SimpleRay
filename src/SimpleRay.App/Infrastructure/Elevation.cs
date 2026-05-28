using System.Diagnostics;
using System.Security.Principal;

namespace SimpleRay.App.Infrastructure;

/// <summary>Admin-rights detection and self-relaunch (TUN requires elevation).</summary>
public static class Elevation
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relaunches the current executable with a UAC elevation prompt and exits
    /// this instance. Returns false if the user declined the prompt.
    /// </summary>
    public static bool RelaunchElevated()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        };

        try
        {
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ERROR_CANCELLED: user dismissed the UAC dialog.
            return false;
        }
    }
}
