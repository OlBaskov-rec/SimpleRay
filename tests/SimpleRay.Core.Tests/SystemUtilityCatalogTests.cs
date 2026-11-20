using System;
using System.Linq;
using SimpleRay.Core.Config;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class SystemUtilityCatalogTests
{
    [Fact]
    public void ProcessNames_AreUnique_AndStoredWithoutExe()
    {
        var names = SystemUtilityCatalog.All.Select(u => u.Process).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n =>
        {
            Assert.False(string.IsNullOrWhiteSpace(n));
            Assert.False(n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void EveryCategory_HasUtilities_AndRdpIsThere()
    {
        foreach (var category in Enum.GetValues<SystemUtilityCategory>())
            Assert.Contains(SystemUtilityCatalog.All, u => u.Category == category);
        Assert.Contains(SystemUtilityCatalog.All, u => u.Process == "mstsc" && u.Category == SystemUtilityCategory.RemoteAccess);
    }

    [Fact]
    public void CatalogNames_BecomeExeProcessRules()
    {
        // A utility picked from the catalog ends up as "<name>.exe" in sing-box's process_name rule.
        var routing = new RoutingSettings { Mode = RoutingMode.Rule, DirectProcesses = { "mstsc" }, ProxyProcesses = { "WinStore.App" } };
        var json = SingBoxConfigGenerator.GenerateJson(ShareLinkParser.Parse("trojan://pw@b.example.com:8443#B"), routing);
        Assert.Contains("\"mstsc.exe\"", json);
        Assert.Contains("\"WinStore.App.exe\"", json);
    }
}
