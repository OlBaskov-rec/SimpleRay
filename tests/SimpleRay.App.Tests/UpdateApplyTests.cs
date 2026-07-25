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
}
