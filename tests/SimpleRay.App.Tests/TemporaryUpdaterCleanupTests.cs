using System;
using SimpleRay.App.Services;

namespace SimpleRay.App.Tests;

public class TemporaryUpdaterCleanupTests
{
    [Theory]
    [InlineData("simpleray-updater-0123456789abcdef0123456789abcdef.exe", 301, true)]
    [InlineData("SIMPLERAY-UPDATER-0123456789ABCDEF0123456789ABCDEF.EXE", 301, true)]
    [InlineData("simpleray-updater-0123456789abcdef0123456789abcdef.exe", 300, false)]
    [InlineData("simpleray-updater-0123456789abcdef0123456789abcdef.exe", 299, false)]
    [InlineData("simpleray-updater-0123456789abcdef0123456789abcdef.exe", -60, false)]
    [InlineData("simpleray-updater-not-a-guid.exe", 600, false)]
    [InlineData("simpleray-updater-.exe", 600, false)]
    [InlineData("simpleray-updater-01234567-89ab-cdef-0123-456789abcdef.exe", 600, false)]
    [InlineData("simpleray-updater-0123456789abcdef0123456789abcdef.exe.bak", 600, false)]
    [InlineData("other-updater-0123456789abcdef0123456789abcdef.exe", 600, false)]
    [InlineData("SimpleRay.exe", 600, false)]
    [InlineData("", 600, false)]
    public void OnlyExactUpdaterNames_OlderThanFiveMinutes_AreEligible(string name, int ageSeconds, bool expected)
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, UpdateService.IsStaleTemporaryUpdater(name, now.AddSeconds(-ageSeconds), now));
    }
}
