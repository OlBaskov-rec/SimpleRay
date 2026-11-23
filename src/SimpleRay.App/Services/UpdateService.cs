using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>Downloads the zip, verifies it, extracts to a clean staging dir; returns its path.</summary>
    public async Task<string> DownloadAndStageAsync(ReleaseInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var workDir = Path.Combine(AppPaths.DataDir, "update");
        if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
        Directory.CreateDirectory(workDir);

        var zipPath = Path.Combine(workDir, "update.zip");
        await DownloadFileAsync(info.ZipUrl, zipPath, progress, ct).ConfigureAwait(false);

        await VerifyDownloadAsync(info, zipPath, ct).ConfigureAwait(false);

        var stagingDir = Path.Combine(workDir, "staging");
        Directory.CreateDirectory(stagingDir);
        ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);
        if (!File.Exists(Path.Combine(stagingDir, "SimpleRay.exe")))
            throw new InvalidOperationException("В архиве обновления нет SimpleRay.exe.");
        return stagingDir;
    }

    /// <summary>
    /// Verifies the downloaded zip. Once a signing key is embedded, a valid signature is
    /// mandatory (a compromised release cannot forge one); before that, the SHA-256 sidecar
    /// is used, which still guards against a corrupted download.
    /// </summary>
    private async Task VerifyDownloadAsync(ReleaseInfo info, string zipPath, CancellationToken ct)
    {
        if (UpdateSignature.IsConfigured)
        {
            if (string.IsNullOrEmpty(info.SigUrl))
                throw new InvalidOperationException(
                    "Обновление не подписано — отклонено. Ожидается файл .sig рядом с архивом.");

            var signature = await Http.GetByteArrayAsync(info.SigUrl, ct).ConfigureAwait(false);
            await using var zip = File.OpenRead(zipPath);
            if (!UpdateSignature.Verify(zip, signature))
                throw new InvalidOperationException("Подпись обновления неверна — обновление отклонено.");
            return;
        }

        if (string.IsNullOrEmpty(info.Sha256Url))
            throw new InvalidOperationException("У обновления нет ни подписи, ни контрольной суммы — отклонено.");

        var expected = await DownloadShaAsync(info.Sha256Url, ct).ConfigureAwait(false);
        var actual = Sha256File(zipPath);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Контрольная сумма не совпала — обновление отклонено.\nОжидалось: {expected}\nПолучено: {actual}");
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
        psi.ArgumentList.Add(ComputeStagingManifest(stagingDir)); // apply-time integrity re-check
        Process.Start(psi);
    }

    /// <summary>Only stale temp updater copies are eligible; never touch a recent updater.</summary>
    internal static bool IsStaleTemporaryUpdater(string fileName, DateTime createdUtc, DateTime nowUtc)
    {
        const string prefix = "simpleray-updater-";
        const string suffix = ".exe";
        return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(fileName.Substring(prefix.Length,
                fileName.Length - prefix.Length - suffix.Length), "N", out _)
            && nowUtc - createdUtc > TimeSpan.FromMinutes(5);
    }

    internal static void CleanupTemporaryUpdaters()
    {
        try
        {
            var now = DateTime.UtcNow;
            foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), "simpleray-updater-*.exe"))
            {
                try
                {
                    // File.Copy preserves LastWriteTime: age must refer to the temp copy itself.
                    if (IsStaleTemporaryUpdater(Path.GetFileName(file), File.GetCreationTimeUtc(file), now))
                        File.Delete(file);
                }
                catch { /* busy or inaccessible: retry on a later startup */ }
            }
        }
        catch { /* temp directory may be inaccessible */ }
    }

    // --- updater branch (runs in the temp copy) ---------------------------

    /// <summary>args: --apply-update &lt;staging&gt; &lt;targetDir&gt; &lt;relaunchExe&gt; &lt;waitPid&gt; [&lt;stagingManifest&gt;]</summary>
    public static void RunApplyUpdate(string[] args)
    {
        if (args.Length < 5) return;
        var staging = args[1];
        var target = args[2];
        var relaunch = args[3];
        int.TryParse(args[4], out var waitPid);
        var expectedManifest = args.Length >= 6 ? args[5] : null;

        WaitForExit(waitPid, TimeSpan.FromSeconds(30));

        // Re-verify the staged payload right before applying it. The zip was authenticated at
        // download time, but the extracted staging dir lives in a user-writable location; a local
        // process could tamper it between staging and this (possibly elevated) apply. If the
        // manifest no longer matches what we staged, refuse to apply and relaunch the current build.
        if (expectedManifest is not null && !ManifestMatches(staging, expectedManifest))
        {
            using var current = TryStart(relaunch, target);
            LogOutcome("ManifestRejected", new InvalidDataException("The staging manifest does not match."));
            return;
        }

        ApplyUpdate(staging, target, relaunch);
    }

    internal enum ApplyResult
    {
        Applied,
        RolledBack, // Files restored; any relaunch failure is recorded in the log.
        RollbackFailed,
        AppliedWithoutBackup,
        ApplyFailedWithoutBackup
    }

    internal static ApplyResult ApplyUpdate(string staging, string target, string relaunch,
        Func<string, string, Process?>? start = null,
        Action<string, string>? copyFile = null,
        Func<Process, TimeSpan, bool>? healthy = null)
    {
        start ??= StartProcess;
        healthy ??= HealthyAfter;
        var backup = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".backup";
        var backedUp = false;
        Exception? error = null;
        Process? started = null;

        ApplyResult Finish(ApplyResult result, Exception? failure = null)
        {
            if (failure is not null)
                error = error is null ? failure : new AggregateException(error, failure);
            LogOutcome(result.ToString(), error);
            return result;
        }

        try
        {
            // A failed deletion must not leave stale files in the new snapshot.
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Directory.CreateDirectory(backup);
            CopyOver(target, backup, copyFile);
            backedUp = true;
        }
        catch (Exception ex) { error = ex; }

        try
        {
            try
            {
                CopyOver(staging, target, copyFile);
                started = start(relaunch, target);
                if (started is null)
                    throw new InvalidOperationException("The updated build did not start.");
                if (!healthy(started, TimeSpan.FromSeconds(8)))
                    throw new InvalidOperationException("The updated build failed its startup health check.");
            }
            catch (Exception ex)
            {
                error = error is null ? ex : new AggregateException(error, ex);
                if (!backedUp) return Finish(ApplyResult.ApplyFailedWithoutBackup);

                // Release executable handles before restoring the previous installation.
                try
                {
                    if (started is not null && !started.HasExited)
                    {
                        started.Kill(entireProcessTree: true);
                        started.WaitForExit(5000);
                    }
                }
                catch { /* restoration will surface any remaining file locks */ }

                try
                {
                    CopyOver(backup, target, copyFile);
                    // Copy-over alone would leave files and directories added by the new build.
                    foreach (var file in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
                        if (!File.Exists(Path.Combine(backup, Path.GetRelativePath(target, file))))
                            File.Delete(file);
                    foreach (var dir in Directory.GetDirectories(target, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length))
                        if (!Directory.Exists(Path.Combine(backup, Path.GetRelativePath(target, dir))))
                            Directory.Delete(dir);
                }
                catch (Exception restoreError)
                {
                    // Keep the snapshot available for manual recovery.
                    return Finish(ApplyResult.RollbackFailed, restoreError);
                }

                try
                {
                    using var previous = start(relaunch, target);
                    if (previous is null)
                        throw new InvalidOperationException("The restored build did not start.");
                }
                catch (Exception launchError)
                {
                    error = new AggregateException(error, launchError);
                }
                SafeDeleteDir(backup);
                return Finish(ApplyResult.RolledBack);
            }

            SafeDeleteDir(backup);
            return Finish(backedUp ? ApplyResult.Applied : ApplyResult.AppliedWithoutBackup);
        }
        finally { started?.Dispose(); }
    }

    private static void LogOutcome(string outcome, Exception? error)
    {
        try
        {
            var detail = error?.ToString().Replace("\r", " ").Replace("\n", " ") ?? "none";
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "SimpleRay-update.log"),
                $"{DateTime.UtcNow:O} {outcome}: {detail}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch { /* diagnostics must never prevent update or recovery */ }
    }

    private static Process? StartProcess(string exe, string workingDir) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            WorkingDirectory = workingDir,
        });

    private static Process? TryStart(string exe, string workingDir)
    {
        try { return StartProcess(exe, workingDir); }
        catch (Exception ex)
        {
            LogOutcome("LaunchFailed", ex);
            return null;
        }
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

    private static void CopyOver(string sourceDir, string targetDir, Action<string, string>? copyFile = null)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, dir)));
        foreach (var src in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, src);
            var dst = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            (copyFile ?? CopyWithRetry)(src, dst);
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

    /// <summary>
    /// A deterministic digest over a directory tree: for every file (sorted by its normalized
    /// relative path) the path and its SHA-256 are folded into one hash. Any added, removed,
    /// renamed or modified file changes the result, so it detects post-staging tampering.
    /// </summary>
    public static string ComputeStagingManifest(string dir)
    {
        var entries = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => (rel: Path.GetRelativePath(dir, f).Replace('\\', '/'), full: f))
            .OrderBy(e => e.rel, StringComparer.Ordinal)
            .ToList();

        var acc = new StringBuilder();
        foreach (var (rel, full) in entries)
            acc.Append(rel).Append('\0').Append(Sha256File(full)).Append('\n');

        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(acc.ToString())));
    }

    private static bool ManifestMatches(string stagingDir, string expected)
    {
        try { return string.Equals(ComputeStagingManifest(stagingDir), expected, StringComparison.OrdinalIgnoreCase); }
        catch { return false; } // can't verify → fail safe, don't apply
    }
}
