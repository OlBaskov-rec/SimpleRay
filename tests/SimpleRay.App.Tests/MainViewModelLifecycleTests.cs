using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SimpleRay.App.Engine;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Localization;
using SimpleRay.App.Services;
using SimpleRay.App.ViewModels;
using SimpleRay.Core.Engine;
using SimpleRay.Core.Models;

namespace SimpleRay.App.Tests;

/// <summary>
/// MainViewModel built from test dependencies: no user data, no network, no real sing-box.
/// Covers the explicit lifecycle (no background work until Start, all of it stopped by
/// ShutdownAsync) and the main window's bindings against a real view model.
/// </summary>
[Collection(GlobalLocalizationCollection.Name)]
public sealed class MainViewModelLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sraytest_" + Guid.NewGuid().ToString("N"));

    public MainViewModelLifecycleTests()
    {
        Directory.CreateDirectory(_dir);
        // Safety net: anything still reaching for AppPaths lands in the temp dir, not %AppData%.
        AppPaths.DataDirOverride = Path.Combine(_dir, "data");
    }

    public void Dispose()
    {
        AppPaths.DataDirOverride = null;
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }

    private sealed class IdleEngine : IVpnEngine
    {
        public EngineState State => EngineState.Stopped;
        public event EventHandler<EngineState>? StateChanged { add { } remove { } }
        public event EventHandler<string>? LogReceived { add { } remove { } }
        public Task StartAsync(string configJson, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool Confirm(string message, string title) => false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The engine and kill-switch are owned by the ConnectionController; ShutdownAsync disposes them.")]
    private MainViewModelDependencies Deps() => new()
    {
        Profiles = new ProfileStore(Path.Combine(_dir, "profiles.json")),
        Settings = new SettingsStore(Path.Combine(_dir, "settings.json")),
        Subscriptions = new SubscriptionStore(Path.Combine(_dir, "subscriptions.json")),
        Updates = new UpdateService(),
        SubscriptionService = new SubscriptionService(),
        Connection = new ConnectionController(new IdleEngine(), new NoOpKillSwitch()),
        Dialogs = new NoDialogs(),
        Dispatcher = Dispatcher.CurrentDispatcher,
        Import = new ImportCoordinator(),
        ProxyProbe = new ProxyProbe(coreExe: Path.Combine(_dir, "none.exe"), workDir: Path.Combine(_dir, "probe")),
    };

    [Fact]
    public void Constructor_ReadsInjectedStores_AndLeavesUserDataAlone()
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleRay");
        var before = Snapshot(userData);
        new ProfileStore(Path.Combine(_dir, "profiles.json")).Save(new List<ProfileConfig>
        {
            new() { Tag = "Test server", Protocol = ProxyProtocol.VLESS, Server = "t.example", Port = 443, Uuid = "u", Raw = "vless://t" },
        });

        WindowXaml.RunOnSta(() =>
        {
            var vm = new MainViewModel(Deps());
            Assert.Equal("Test server", Assert.Single(vm.Profiles).Tag);
            ShutdownLikeMainWindow(vm);
        });

        Assert.Equal(before, Snapshot(userData));
    }

    [Fact]
    public void BackgroundWork_RunsOnlyBetweenStartAndShutdown()
    {
        WindowXaml.RunOnSta(() =>
        {
            var vm = new MainViewModel(Deps());
            Assert.False(vm.SubscriptionTimerRunning);
            Assert.False(vm.UpdateTimerRunning);

            vm.Start();
            Assert.True(vm.SubscriptionTimerRunning);
            Assert.True(vm.UpdateTimerRunning);

            ShutdownLikeMainWindow(vm);
            Assert.False(vm.SubscriptionTimerRunning);
            Assert.False(vm.UpdateTimerRunning);

            // Repeated shutdown and a late Start are harmless no-ops.
            ShutdownLikeMainWindow(vm);
            vm.Start();
            Assert.False(vm.SubscriptionTimerRunning);
            Assert.False(vm.UpdateTimerRunning);
        });
    }

    [Fact]
    public void LanguageChange_ReachesTheViewModel_OnlyUntilShutdown()
    {
        WindowXaml.RunOnSta(() =>
        {
            var loc = LocalizationManager.Instance;
            var original = loc.CurrentCulture;
            var other = original == "ru" ? "en" : "ru";
            try
            {
                var vm = new MainViewModel(Deps());
                var changed = new List<string?>();
                vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

                loc.SetCulture(other, persist: false);
                Assert.Contains(nameof(MainViewModel.ConnectButtonText), changed);

                ShutdownLikeMainWindow(vm);
                changed.Clear();
                loc.SetCulture(original, persist: false);
                Assert.Empty(changed);
            }
            finally
            {
                loc.SetCulture(original, persist: false);
            }
        });
    }

    [Fact]
    public void MainWindowBindings_ResolveAgainstTheViewModel()
    {
        var errors = new List<string>();
        WindowXaml.RunOnSta(() =>
        {
            // Without a debugger WPF traces bindings only after a refresh; it resets the switch,
            // so it goes first.
            PresentationTraceSources.Refresh();
            var listener = new CollectingListener(errors);
            var source = PresentationTraceSources.DataBindingSource;
            var oldLevel = source.Switch.Level;
            source.Listeners.Add(listener);
            source.Switch.Level = SourceLevels.Error;
            try
            {
                var vm = new MainViewModel(Deps());
                var window = WindowXaml.Load("MainWindow");
                window.DataContext = vm;
                window.Measure(new Size(1060, 820));
                window.Arrange(new Rect(0, 0, 1060, 820));
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                window.Close();
                ShutdownLikeMainWindow(vm);
            }
            finally
            {
                source.Listeners.Remove(listener);
                source.Switch.Level = oldLevel;
            }
        });

        Assert.True(errors.Count == 0, "Binding errors:\n" + string.Join("\n", errors));
    }

    /// <summary>
    /// Blocks the UI thread on shutdown exactly as MainWindow.Closed does; it must not deadlock,
    /// which is part of what these tests check.
    /// </summary>
    private static void ShutdownLikeMainWindow(MainViewModel vm) =>
        vm.ShutdownAsync().GetAwaiter().GetResult();

    private sealed class CollectingListener(List<string> sink) : TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null) sink.Add(message);
        }
    }

    private static Dictionary<string, DateTime> Snapshot(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .ToDictionary(f => f, File.GetLastWriteTimeUtc)
            : new Dictionary<string, DateTime>();
}
