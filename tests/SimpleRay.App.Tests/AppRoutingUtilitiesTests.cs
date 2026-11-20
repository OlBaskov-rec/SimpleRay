using System.Linq;
using SimpleRay.App.ViewModels;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Tests;

public class AppRoutingUtilitiesTests
{
    [Fact]
    public void Groups_FollowTheCatalog_AndHaveDescriptions()
    {
        var vm = new AppRoutingViewModel(new RoutingSettings());
        var groups = vm.UtilityGroups();

        Assert.Equal(3, groups.Count);
        var items = groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(SystemUtilityCatalog.All.Count, items.Count);
        Assert.All(items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Description));
            Assert.DoesNotContain("util.", i.Description); // a missing translation key would show itself
        });
        Assert.Contains(items, i => i.Process == "mstsc" && i.Header.Contains("mstsc.exe"));
    }

    [Fact]
    public void Items_ShowWhichListTheyAreIn()
    {
        var routing = new RoutingSettings { DirectProcesses = { "MSTSC" }, ProxyProcesses = { "ssh" } };
        var items = new AppRoutingViewModel(routing).UtilityGroups().SelectMany(g => g.Items).ToList();

        Assert.NotNull(items.Single(i => i.Process == "mstsc").Placement); // case-insensitive match
        Assert.NotNull(items.Single(i => i.Process == "ssh").Placement);
        Assert.Null(items.Single(i => i.Process == "curl").Placement);
        Assert.NotEqual(items.Single(i => i.Process == "mstsc").Placement, items.Single(i => i.Process == "ssh").Placement);
    }

    [Fact]
    public void Picking_FillsTheName_ThenTheUsualButtonRoutesIt()
    {
        var routing = new RoutingSettings();
        var vm = new AppRoutingViewModel(routing);

        vm.PickUtility("mstsc");
        Assert.Equal("mstsc", vm.ManualName);
        Assert.True(vm.AddDirectCommand.CanExecute(null));

        vm.AddDirectCommand.Execute(null);
        Assert.Contains("mstsc", routing.DirectProcesses);
    }
}
