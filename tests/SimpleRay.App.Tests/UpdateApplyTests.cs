using System;
using System.Diagnostics;
using System.IO;
using SimpleRay.App.Services;

namespace SimpleRay.App.Tests;

public class UpdateApplyTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "sraytest_" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_base, true); } catch { } }

    [Fact]
    public void RunApplyUpdate_RollsBack_WhenExecutableIsMissing()
    {
        var (staging, target) = CreateInstall();
        UpdateService.RunApplyUpdate(new[]
        {
            "--apply-update", staging, target, Path.Combine(_base, "missing.exe"), "999999"
        });
        AssertOriginal(target);
        Assert.False(Directory.Exists(target + ".backup"));
    }

    [Fact]
    public void ApplyUpdate_RollsBack_WhenSecondCopyFails()
    {
        var (staging, target) = CreateInstall();
        var copies = 0;
        var starts = 0;
        using var process = new Process();
        var result = UpdateService.ApplyUpdate(staging, target, "unused.exe",
            start: (_, _) => { starts++; AssertOriginal(target); return process; },
            copyFile: (src, dst) =>
            {
                if (src.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && ++copies == 2)
                {
                    Assert.Equal("NEW", File.ReadAllText(Path.Combine(target, "SimpleRay.exe")));
                    throw new IOException("Second staged copy failed");
                }
                File.Copy(src, dst, true);
            }, healthy: (_, _) => throw new InvalidOperationException("New build was never started"));
        Assert.Equal(2, copies);
        Assert.Equal(1, starts);
        Assert.Equal(UpdateService.ApplyResult.RolledBack, result);
        AssertOriginal(target);
        Assert.False(Directory.Exists(target + ".backup"));
    }

    [Fact]
    public void ApplyUpdate_RollsBack_WhenStartThrows()
    {
        var (staging, target) = CreateInstall();
        var starts = 0;
        using var process = new Process();
        var result = UpdateService.ApplyUpdate(staging, target, "unused.exe", start: (_, _) =>
        {
            if (++starts == 1) throw new InvalidOperationException("Launch failed");
            AssertOriginal(target);
            return process;
        });
        Assert.Equal(UpdateService.ApplyResult.RolledBack, result);
        Assert.Equal(2, starts);
        AssertOriginal(target);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyUpdate_RollsBack_WhenStartReturnsNullOrBuildIsUnhealthy(bool nullStart)
    {
        var (staging, target) = CreateInstall();
        var starts = 0;
        var healthChecks = 0;
        using var process = new Process();
        var result = UpdateService.ApplyUpdate(staging, target, "unused.exe",
            start: (_, _) => ++starts == 1 && nullStart ? null : process,
            healthy: (_, window) =>
            {
                healthChecks++;
                Assert.Equal(TimeSpan.FromSeconds(8), window);
                return false;
            });
        Assert.Equal(UpdateService.ApplyResult.RolledBack, result);
        Assert.Equal(2, starts);
        Assert.Equal(nullStart ? 0 : 1, healthChecks);
        AssertOriginal(target);
        Assert.False(Directory.Exists(target + ".backup"));
    }

    [Fact]
    public void ApplyUpdate_PreservesBackup_WhenRestoreFails()
    {
        var (staging, target) = CreateInstall();
        using var process = new Process();
        var starts = 0;
        var result = UpdateService.ApplyUpdate(staging, target, "unused.exe",
            start: (_, _) => { starts++; return process; },
            copyFile: (src, dst) =>
            {
                if (src.StartsWith(target + ".backup" + Path.DirectorySeparatorChar))
                    throw new IOException("Restore failed");
                File.Copy(src, dst, true);
            }, healthy: (_, _) => false);
        Assert.Equal(UpdateService.ApplyResult.RollbackFailed, result);
        Assert.Equal(1, starts);
        AssertOriginal(target + ".backup");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyUpdate_ReportsMissingBackup_AndStillCopies(bool copyFails)
    {
        var (staging, target) = CreateInstall();
        using var process = new Process();
        var attempted = false;
        var result = UpdateService.ApplyUpdate(staging, target, "unused.exe",
            start: (_, _) => process,
            copyFile: (src, dst) =>
            {
                if (dst.StartsWith(target + ".backup" + Path.DirectorySeparatorChar))
                    throw new IOException("Backup failed");
                attempted = true;
                if (copyFails) throw new IOException("Apply failed");
                File.Copy(src, dst, true);
            }, healthy: (_, _) => true);
        Assert.True(attempted);
        Assert.Equal(copyFails ? UpdateService.ApplyResult.ApplyFailedWithoutBackup
            : UpdateService.ApplyResult.AppliedWithoutBackup, result);
        Assert.Equal(copyFails ? "OLD" : "NEW", File.ReadAllText(Path.Combine(target, "SimpleRay.exe")));
    }

    private (string staging, string target) CreateInstall()
    {
        var staging = Path.Combine(_base, "staging");
        var target = Path.Combine(_base, "target");
        Directory.CreateDirectory(Path.Combine(staging, "new-dir"));
        Directory.CreateDirectory(Path.Combine(target, "empty-old-dir"));
        File.WriteAllText(Path.Combine(target, "SimpleRay.exe"), "OLD");
        File.WriteAllText(Path.Combine(target, "old.txt"), "keep");
        File.WriteAllText(Path.Combine(staging, "SimpleRay.exe"), "NEW");
        File.WriteAllText(Path.Combine(staging, "new-dir", "added.txt"), "added");
        return (staging, target);
    }

    private static void AssertOriginal(string target)
    {
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(target, "SimpleRay.exe")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "old.txt")));
        Assert.Equal(2, Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(new[] { Path.Combine(target, "empty-old-dir") }, Directory.GetDirectories(target));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(target, "empty-old-dir")));
    }

    [Fact]
    public void RunApplyUpdate_CopiesStagingOverTarget_PreservingExtras()
    {
        var staging = Path.Combine(_base, "staging");
        var target = Path.Combine(_base, "target");
        Directory.CreateDirectory(Path.Combine(staging, "core"));
        Directory.CreateDirectory(target);

        File.WriteAllText(Path.Combine(staging, "SimpleRay.exe"), "NEW");
        File.WriteAllText(Path.Combine(staging, "new.txt"), "added");
        File.WriteAllText(Path.Combine(staging, "core", "sing-box.exe"), "newcore");
        File.WriteAllText(Path.Combine(target, "old.txt"), "keep");        // must survive (copy-over)
        File.WriteAllText(Path.Combine(target, "SimpleRay.exe"), "OLD");    // must be overwritten

        using var process = new Process();
        var result = UpdateService.ApplyUpdate(staging, target, "unused.exe",
            start: (_, _) => process, healthy: (_, _) => true);
        Assert.Equal(UpdateService.ApplyResult.Applied, result);
        Assert.False(Directory.Exists(target + ".backup"));

        Assert.Equal("NEW", File.ReadAllText(Path.Combine(target, "SimpleRay.exe")));
        Assert.Equal("added", File.ReadAllText(Path.Combine(target, "new.txt")));
        Assert.Equal("newcore", File.ReadAllText(Path.Combine(target, "core", "sing-box.exe")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "old.txt")));
    }

    [Fact]
    public void RunApplyUpdate_RollsBack_WhenNewBuildExitsNonZero()
    {
        var staging = Path.Combine(_base, "staging");
        var target = Path.Combine(_base, "target");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(target);

        File.WriteAllText(Path.Combine(target, "SimpleRay.exe"), "OLD");
        File.WriteAllText(Path.Combine(target, "old.txt"), "keep");
        File.WriteAllText(Path.Combine(staging, "SimpleRay.exe"), "NEW");

        // Relaunch target that exits with code 1 → treated as a crash → rollback.
        var fail = Path.Combine(_base, "fail.cmd");
        File.WriteAllText(fail, "@echo off\r\nexit /b 1\r\n");

        UpdateService.RunApplyUpdate(new[] { "--apply-update", staging, target, fail, "999999" });

        // The previous build is restored.
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(target, "SimpleRay.exe")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "old.txt")));
        Assert.False(Directory.Exists(target + ".backup")); // backup cleaned up
    }

    [Fact]
    public void RunApplyUpdate_TooFewArgs_DoesNothing()
    {
        // Should return without throwing.
        UpdateService.RunApplyUpdate(new[] { "--apply-update", "only-one" });
    }

    [Fact]
    public void RunApplyUpdate_Applies_WhenManifestMatches()
    {
        var staging = Path.Combine(_base, "staging");
        var target = Path.Combine(_base, "target");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(staging, "SimpleRay.exe"), "NEW");
        File.WriteAllText(Path.Combine(target, "SimpleRay.exe"), "OLD");

        var success = Path.Combine(_base, "success.cmd");
        File.WriteAllText(success, "@echo off\r\nexit /b 0\r\n");
        var manifest = UpdateService.ComputeStagingManifest(staging);
        UpdateService.RunApplyUpdate(new[]
        {
            "--apply-update", staging, target, success, "999999", manifest
        });

        Assert.Equal("NEW", File.ReadAllText(Path.Combine(target, "SimpleRay.exe")));
    }

    [Fact]
    public void RunApplyUpdate_RefusesApply_WhenStagingTamperedAfterManifest()
    {
        var staging = Path.Combine(_base, "staging");
        var target = Path.Combine(_base, "target");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(staging, "SimpleRay.exe"), "NEW");
        File.WriteAllText(Path.Combine(target, "SimpleRay.exe"), "OLD");

        var manifest = UpdateService.ComputeStagingManifest(staging);
        // A local process tampers the staged payload AFTER we captured the manifest.
        File.WriteAllText(Path.Combine(staging, "SimpleRay.exe"), "EVIL");

        UpdateService.RunApplyUpdate(new[]
        {
            "--apply-update", staging, target, Path.Combine(_base, "nope.exe"), "999999", manifest
        });

        // Tamper detected → the install is left untouched (never overwritten with EVIL).
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(target, "SimpleRay.exe")));
    }
}
