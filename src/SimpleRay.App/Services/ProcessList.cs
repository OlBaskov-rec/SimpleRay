using System.Diagnostics;

namespace SimpleRay.App.Services;

/// <summary>Enumerates running process names for the per-app routing picker.</summary>
public static class ProcessList
{
    /// <summary>
    /// Distinct, sorted lowercase process names (without ".exe") of currently
    /// running processes. Best-effort: processes that vanish or deny access are skipped.
    /// </summary>
    public static IReadOnlyList<string> RunningNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var n = p.ProcessName;
                if (!string.IsNullOrWhiteSpace(n))
                    names.Add(n);
            }
            catch
            {
                // process exited or access denied — ignore
            }
            finally
            {
                p.Dispose();
            }
        }
        return names.ToList();
    }
}
