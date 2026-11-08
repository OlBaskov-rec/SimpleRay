using System.Diagnostics;

namespace SimpleRay.App.Infrastructure;

/// <summary>Opens web links in the user's default browser.</summary>
public static class UrlLauncher
{
    /// <summary>
    /// Opens an http(s) URL; anything else is ignored. Goes through explorer.exe rather than
    /// ShellExecute on the URL: the app usually runs elevated (TUN), and explorer hands the link
    /// to the already-running, non-elevated shell, so the browser doesn't start as admin.
    /// </summary>
    public static bool TryOpen(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + uri.AbsoluteUri + "\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
