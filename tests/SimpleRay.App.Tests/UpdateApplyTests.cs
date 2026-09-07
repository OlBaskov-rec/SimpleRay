using System;
using System.IO;
using SimpleRay.App.Services;

namespace SimpleRay.App.Tests;

public class UpdateApplyTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "sraytest_" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_base, true); } catch { } }

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

        // Bogus relaunch path (Process.Start throws → swallowed) and a pid that isn't running.
        UpdateService.RunApplyUpdate(new[]
        {
            "--apply-update", staging, target, Path.Combine(_base, "nope.exe"), "999999"
        });

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

        var manifest = UpdateService.ComputeStagingManifest(staging);
        UpdateService.RunApplyUpdate(new[]
        {
            "--apply-update", staging, target, Path.Combine(_base, "nope.exe"), "999999", manifest
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
