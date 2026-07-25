using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SimpleRay.App.Infrastructure;
using SimpleRay.Core.Update;

namespace SimpleRay.App.Services;

/// <summary>
/// Consent-based self-update from GitHub Releases. Checks the latest release,
/// downloads + SHA256-verifies the portable zip, stages it, then applies it via a
/// temp copy of the exe that waits for this process to exit, swaps files and relaunches.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/OlBaskov-rec/SimpleRay/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SimpleRay-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static Version CurrentVersion =>
        ReleaseParser.Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0));

    /// <summary>Returns a newer release, or null if up to date / no releases yet.</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null; // repo has no releases yet
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ReleaseParser.TryParseLatest(json, CurrentVersion, out var info) ? info : null;
    }

    /// <summary>Downloads the zip, verifies SHA256, extracts to a clean staging dir; returns its path.</summary>
    public async Task<string> DownloadAndStageAsync(ReleaseInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var workDir = Path.Combine(AppPaths.DataDir, "update");
        if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
        Directory.CreateDirectory(workDir);

        var zipPath = Path.Combine(workDir, "update.zip");
        await DownloadFileAsync(info.ZipUrl, zipPath, progress, ct).ConfigureAwait(false);

        var expected = await DownloadShaAsync(info.Sha256Url, ct).ConfigureAwait(false);
        var actual = Sha256File(zipPath);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Контрольная сумма не совпала — обновление отклонено.\nОжидалось: {expected}\nПолучено: {actual}");

        var stagingDir = Path.Combine(workDir, "staging");
        Directory.CreateDirectory(stagingDir);
        ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);
        if (!File.Exists(Path.Combine(stagingDir, "SimpleRay.exe")))
            throw new InvalidOperationException("В архиве обновления нет SimpleRay.exe.");
        return stagingDir;
    }

    /// <summary>Launches a temp copy of the exe as the updater; caller should then exit.</summary>
    public void ApplyAndRestart(string stagingDir)
    {
        var appDir = AppPaths.BaseDir.TrimEnd(Path.DirectorySeparatorChar);
        var selfExe = Path.Combine(appDir, "SimpleRay.exe");
        var updaterExe = Path.Combine(Path.GetTempPath(), $"simpleray-updater-{Guid.NewGuid():N}.exe");
        File.Copy(selfExe, updaterExe, overwrite: true);

        var psi = new ProcessStartInfo { FileName = updaterExe, UseShellExecute = false };
        psi.ArgumentList.Add("--apply-update");
        psi.ArgumentList.Add(stagingDir);
        psi.ArgumentList.Add(appDir);
        psi.ArgumentList.Add(selfExe);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(psi);
    }

    // --- updater branch (runs in the temp copy) ---------------------------

    /// <summary>args: --apply-update &lt;staging&gt; &lt;targetDir&gt; &lt;relaunchExe&gt; &lt;waitPid&gt;</summary>
    public static void RunApplyUpdate(string[] args)
    {
        if (args.Length < 5) return;
        var staging = args[1];
        var target = args[2];
        var relaunch = args[3];
        int.TryParse(args[4], out var waitPid);

        WaitForExit(waitPid, TimeSpan.FromSeconds(30));

        // Snapshot the current install so a broken new build can be rolled back.
        var backup = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".backup";
        var backedUp = false;
        try
        {
            SafeDeleteDir(backup);
            CopyOver(target, backup);
            backedUp = true;
        }
        catch { /* couldn't back up → we just won't be able to roll back */ }

        CopyOver(staging, target);

        var started = TryStart(relaunch, target);
        if (backedUp && started is not null && !HealthyAfter(started, TimeSpan.FromSeconds(8)))
        {
            // The new build exited abnormally on startup — restore the previous version.
            try { if (!started.HasExited) started.Kill(entireProcessTree: true); } catch { }
            try { CopyOver(backup, target); } catch { }
            TryStart(relaunch, target);
        }

        SafeDeleteDir(backup);
    }

    private static Process? TryStart(string exe, string workingDir)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = workingDir,
            });
        }
        catch { return null; }
    }

    /// <summary>Healthy if still running after <paramref name="window"/>, or it exited cleanly (0). A non-zero exit = crash.</summary>
    private static bool HealthyAfter(Process p, TimeSpan window)
    {
        try
        {
            return p.WaitForExit((int)window.TotalMilliseconds) ? p.ExitCode == 0 : true;
        }
        catch
        {
            return true; // can't tell — assume healthy, don't roll back
        }
    }

    private static void SafeDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best-effort */ }
    }

    private static void WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch { /* already gone */ }
        Thread.Sleep(500); // grace period for file handles to release
    }

    private static void CopyOver(string sourceDir, string targetDir)
    {
        foreach (var src in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, src);
            var dst = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            CopyWithRetry(src, dst);
        }
    }

    private static void CopyWithRetry(string src, string dst)
    {
        for (int i = 0; i < 9; i++)
        {
            try { File.Copy(src, dst, overwrite: true); return; }
            catch (IOException) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) { Thread.Sleep(500); }
        }
        File.Copy(src, dst, overwrite: true); // final try surfaces the error
    }

    // --- helpers ----------------------------------------------------------

    private static async Task DownloadFileAsync(string url, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;

        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(dest);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
    }

    private static async Task<string> DownloadShaAsync(string url, CancellationToken ct)
    {
        var text = (await Http.GetStringAsync(url, ct).ConfigureAwait(false)).Trim();
        var tokens = text.Split(new[] { ' ', '\t', '*', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 ? tokens[0] : text;
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
