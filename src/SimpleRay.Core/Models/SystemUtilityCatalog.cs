namespace SimpleRay.Core.Models;

public enum SystemUtilityCategory
{
    /// <summary>Remote desktop and remote shells (RDP, SSH…).</summary>
    RemoteAccess,
    /// <summary>Command-line network tools and shells.</summary>
    CommandLine,
    /// <summary>Apps that ship with Windows.</summary>
    BuiltInApps,
}

/// <summary>
/// A standard Windows utility that people often want to route on its own — for example Remote
/// Desktop straight to an office server, or SSH through the VPN. <see cref="Process"/> is the
/// executable name without ".exe", exactly as the per-app lists store it.
/// </summary>
public sealed record SystemUtility(string Process, string DisplayName, SystemUtilityCategory Category);

/// <summary>
/// Utilities offered by "Standard utilities" in the per-app routing window. Windows only:
/// SimpleRay runs on Windows, and sing-box matches these by Windows process name.
/// </summary>
public static class SystemUtilityCatalog
{
    public static IReadOnlyList<SystemUtility> All { get; } = new SystemUtility[]
    {
        new("mstsc", "Remote Desktop (RDP)", SystemUtilityCategory.RemoteAccess),
        new("msra", "Remote Assistance", SystemUtilityCategory.RemoteAccess),
        new("quickassist", "Quick Assist", SystemUtilityCategory.RemoteAccess),
        new("ssh", "OpenSSH (ssh)", SystemUtilityCategory.RemoteAccess),
        new("scp", "OpenSSH copy (scp)", SystemUtilityCategory.RemoteAccess),
        new("sftp", "OpenSSH SFTP (sftp)", SystemUtilityCategory.RemoteAccess),

        new("curl", "curl", SystemUtilityCategory.CommandLine),
        new("ftp", "FTP", SystemUtilityCategory.CommandLine),
        new("telnet", "Telnet", SystemUtilityCategory.CommandLine),
        new("powershell", "Windows PowerShell", SystemUtilityCategory.CommandLine),
        new("pwsh", "PowerShell 7", SystemUtilityCategory.CommandLine),

        new("msedge", "Microsoft Edge", SystemUtilityCategory.BuiltInApps),
        new("OneDrive", "OneDrive", SystemUtilityCategory.BuiltInApps),
        new("WinStore.App", "Microsoft Store", SystemUtilityCategory.BuiltInApps),
    };
}
