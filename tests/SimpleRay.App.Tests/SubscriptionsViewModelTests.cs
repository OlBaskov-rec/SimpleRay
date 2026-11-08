using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using SimpleRay.App.Services;
using SimpleRay.App.ViewModels;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Tests;

public class SubscriptionsViewModelTests
{
    private sealed class FakeHost : ISubscriptionHost
    {
        public List<SubscriptionEntry> List { get; } = new();
        public List<string> Refreshed { get; } = new();
        public List<string> Removed { get; } = new();

        public IReadOnlyList<SubscriptionEntry> Subscriptions => List;
        public string StatusText => "";
        public int ProfileCount(string url) => url.Length; // any stable number
        public Task AddSubscriptionAsync() => Task.CompletedTask;

        public Task RefreshSubscriptionsAsync(IReadOnlyList<SubscriptionEntry> entries)
        {
            Refreshed.AddRange(entries.Select(e => e.Url));
            return Task.CompletedTask;
        }

        public Task EditSubscriptionAsync(SubscriptionEntry entry) => Task.CompletedTask;

        public void RemoveSubscription(SubscriptionEntry entry)
        {
            Removed.Add(entry.Url);
            List.Remove(entry);
            Raise();
        }

        public void Raise() => SubscriptionsChanged?.Invoke(this, EventArgs.Empty);

        public event EventHandler? SubscriptionsChanged;
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    [Fact]
    public void DisplayName_PrefersUserName_ThenProviderTitle_ThenHost()
    {
        var e = new SubscriptionEntry { Url = "https://sub.example.com/abc" };
        Assert.Equal("sub.example.com", e.DisplayName);
        e.Info.Title = "Provider";
        Assert.Equal("Provider", e.DisplayName);
        e.Name = "Mine";
        Assert.Equal("Mine", e.DisplayName);
    }

    [Fact]
    public void EmptyHost_IsEmpty_AndRowCommandsDisabled()
    {
        var vm = new SubscriptionsViewModel(new FakeHost());
        Assert.True(vm.IsEmpty);
        Assert.Null(vm.Selected);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.RefreshAllCommand.CanExecute(null));
        Assert.False(vm.EditCommand.CanExecute(null));
        Assert.False(vm.RemoveCommand.CanExecute(null));
        Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Rows_ShowNameAndDetails_AndSelectFirst()
    {
        var host = new FakeHost();
        host.List.Add(new SubscriptionEntry
        {
            Url = "https://a.example.com/x",
            UserAgent = "Client/1.0",
            Info = new SubscriptionMetadata { Title = "A", Download = 1024, Total = 4096 },
        });
        host.List.Add(new SubscriptionEntry { Url = "https://b.example.com/y" });

        var vm = new SubscriptionsViewModel(host);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("A", vm.Items[0].DisplayName);
        Assert.Contains("a.example.com", vm.Items[0].Details);
        Assert.Contains("User-Agent: Client/1.0", vm.Items[0].Details);
        Assert.Equal("b.example.com", vm.Items[1].DisplayName);
        Assert.Same(vm.Items[0], vm.Selected);
        Assert.True(vm.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void HostChange_KeepsSelectionOnSameUrl()
    {
        var host = new FakeHost();
        host.List.Add(new SubscriptionEntry { Url = "https://a.example.com/x" });
        host.List.Add(new SubscriptionEntry { Url = "https://b.example.com/y" });
        var vm = new SubscriptionsViewModel(host);
        vm.Selected = vm.Items[1];

        host.List[1].Name = "Renamed";
        host.Raise();

        Assert.Equal("https://b.example.com/y", vm.Selected!.Entry.Url);
        Assert.Equal("Renamed", vm.Selected.DisplayName);
    }

    [Fact]
    public void Commands_ActOnSelected()
    {
        var host = new FakeHost();
        host.List.Add(new SubscriptionEntry { Url = "https://a.example.com/x" });
        host.List.Add(new SubscriptionEntry { Url = "https://b.example.com/y" });
        var vm = new SubscriptionsViewModel(host);
        vm.Selected = vm.Items[1];

        vm.RefreshCommand.Execute(null);
        Assert.Equal(new[] { "https://b.example.com/y" }, host.Refreshed);

        vm.RemoveCommand.Execute(null);
        Assert.Equal(new[] { "https://b.example.com/y" }, host.Removed);
        Assert.Single(vm.Items);
        Assert.Equal("https://a.example.com/x", vm.Selected!.Entry.Url); // falls back to the remaining row
    }

    [Fact]
    public void Detach_StopsReloading()
    {
        var host = new FakeHost();
        var vm = new SubscriptionsViewModel(host);
        vm.Detach();
        host.List.Add(new SubscriptionEntry { Url = "https://a.example.com/x" });
        host.Raise();
        Assert.Empty(vm.Items);
    }
}
