using System.Windows;
using System.Windows.Threading;
using SimpleRay.App.Engine;
using SimpleRay.App.Infrastructure;
using SimpleRay.App.Services;

namespace SimpleRay.App.ViewModels;

/// <summary>
/// Everything <see cref="MainViewModel"/> works with. <see cref="CreateDefault"/> builds the real
/// set (user data folder, real sing-box, WFP kill-switch); tests build their own so the view model
/// can be created without touching the user's data or network.
/// </summary>
public sealed class MainViewModelDependencies
{
    public required ProfileStore Profiles { get; init; }
    public required SettingsStore Settings { get; init; }
    public required SubscriptionStore Subscriptions { get; init; }
    public required UpdateService Updates { get; init; }
    public required SubscriptionService SubscriptionService { get; init; }
    public required ConnectionController Connection { get; init; }
    public required IDialogService Dialogs { get; init; }
    public required Dispatcher Dispatcher { get; init; }
    public required ImportCoordinator Import { get; init; }
    public required ProxyProbe ProxyProbe { get; init; }

    public static MainViewModelDependencies CreateDefault(IDialogService? dialogs = null, IKillSwitch? killSwitch = null)
    {
        // WfpKillSwitch validated on a real machine (harness Stage A: Engage/Disengage clean,
        // block confirmed, filters cleaned by deterministic key). Engaged on connect only when
        // the kill-switch setting is on; Engage failures never break the connection, and
        // CleanupLeftovers (App startup) + Disengage (disconnect/exit) + the non-boot-persistent
        // filters make it fail-closed but recoverable. The ConnectionController owns the engine +
        // watchdog + liveness + kill-switch lifecycle; the view-model just maps its events to UI.
        var engine = new SingBoxEngine(new EngineOptions
        {
            ExecutablePath = AppPaths.CoreExe,
            WorkingDirectory = AppPaths.RuntimeDir,
            Terminator = new ConsoleCtrlTerminator(),
        });
        return new MainViewModelDependencies
        {
            Profiles = new ProfileStore(),
            Settings = new SettingsStore(),
            Subscriptions = new SubscriptionStore(),
            Updates = new UpdateService(),
            SubscriptionService = new SubscriptionService(),
            Connection = new ConnectionController(engine, killSwitch ?? new WfpKillSwitch()),
            Dialogs = dialogs ?? new MessageBoxDialogService(),
            Dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher,
            Import = new ImportCoordinator(),
            ProxyProbe = new ProxyProbe(),
        };
    }
}
