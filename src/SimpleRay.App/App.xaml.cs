using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SimpleRay.App.Services;

namespace SimpleRay.App;

public partial class App : Application
{
    /// <summary>
    /// True once the app is really exiting (tray "Exit", update, elevated relaunch, Windows
    /// logoff). Until then the main window's close button only hides it to the tray.
    /// </summary>
    public static bool IsExiting { get; private set; }

    /// <summary>The one way to quit for real: lets the main window close instead of hiding.</summary>
    public static void Quit()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsExiting = true; // logoff/shutdown must not be blocked by hide-to-tray
        base.OnSessionEnding(e);
    }

    static App()
    {
        // App-wide tooltip behaviour: long enough to read the structured tips, and also shown on
        // disabled controls (a greyed-out button should still say what it would do).
        System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(60000));
        System.Windows.Controls.ToolTipService.ShowOnDisabledProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
    }

    public App()
    {
        // Last-resort safety net: an unhandled exception must not silently kill the
        // process — a running sing-box with strict_route would be orphaned and the
        // user left without network. Report, log, and keep the app alive.
        DispatcherUnhandledException += (_, e) =>
        {
            ReportCrash(e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
        // Background-thread exceptions terminate the process; at least record them.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogError(e.ExceptionObject as Exception);
    }

    private static void LogError(Exception? ex)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Infrastructure.AppPaths.DataDir, "last-error.txt"),
                $"{DateTime.Now:O}{Environment.NewLine}{ex}");
        }
        catch { /* crash reporting must never throw */ }
    }

    private static void ReportCrash(Exception ex)
    {
        LogError(ex);
        MessageBox.Show(
            Localization.LocalizationManager.Instance.Format("crash.message", ex.Message),
            Localization.LocalizationManager.Instance["crash.title"],
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Updater branch: this is a temp copy of the exe swapping files into the app dir.
        if (e.Args.Length > 0 && e.Args[0] == "--apply-update")
        {
            try { Services.UpdateService.RunApplyUpdate(e.Args); }
            catch { /* best-effort; nothing we can surface here */ }
            Shutdown();
            return;
        }

        // Diagnostic: verify the (portable/single-file) exe resolves its bundled deps.
        if (e.Args.Contains("--paths"))
        {
            var report =
                $"BaseDir={Infrastructure.AppPaths.BaseDir}\n" +
                $"CoreExe={Infrastructure.AppPaths.CoreExe} exists={File.Exists(Infrastructure.AppPaths.CoreExe)}\n" +
                $"GeoDir={Infrastructure.AppPaths.GeoDir} exists={Directory.Exists(Infrastructure.AppPaths.GeoDir)}\n" +
                $"Version={System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}\n";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "simpleray_paths.txt"), report);
            Shutdown();
            return;
        }

        // Dev-only headless probe of the WinRT camera pipeline (init → frames → BGRA copy).
        if (e.Args.Contains("--camera-selftest"))
        {
            await RunCameraSelfTestAsync();
            Shutdown();
            return;
        }

        // Single instance: if SimpleRay is already running, activate it and don't start a
        // second window. The elevated "--connect" relaunch is exempt — it deliberately replaces
        // the instance that spawned it (which is exiting).
        if (!e.Args.Contains("--connect") && TryActivateRunningInstance())
        {
            Shutdown();
            return;
        }

        // Recovery: kill any stale engine process (sing-box) from a previous run that crashed
        // during connection. The single-instance guard above already verified that no legitimate
        // SimpleRay is running, so any engine process here is orphaned and must be cleaned up
        // before CleanupLeftovers removes our WFP filters (the engine still holds its TUN adapter).
        try { Engine.EngineOrphans.KillStale(Infrastructure.AppPaths.CoreExe); } catch { /* best-effort */ }

        // Recovery: remove any kill-switch filters left over from a previous run that
        // crashed while engaged (fail-closed filters survive the crash). Best-effort and
        // no-op when not elevated or when nothing is present.
        Engine.WfpKillSwitch.CleanupLeftovers();

        // "--connect" is set by the elevated relaunch after a Connect click, so this
        // instance connects automatically instead of making the user click again.
        new MainWindow(connectOnStartup: e.Args.Contains("--connect")).Show();
    }

    /// <summary>
    /// True if another SimpleRay process is already running (so this one should exit). Also makes
    /// a best-effort attempt to restore and focus its window — which succeeds within the same
    /// integrity level (e.g. before the other instance elevated) and is silently skipped across
    /// integrity levels (UIPI), where simply not starting a second window is enough.
    /// </summary>
    private static bool TryActivateRunningInstance()
    {
        try
        {
            var me = Process.GetCurrentProcess();
            var other = Process.GetProcessesByName(me.ProcessName)
                .FirstOrDefault(p => p.Id != me.Id && SameImage(p, me));
            if (other is null)
                return false;

            var h = other.MainWindowHandle;
            if (h != nint.Zero)
            {
                ShowWindow(h, SW_RESTORE);
                SetForegroundWindow(h);
            }
            return true;
        }
        catch { return false; } // never let the guard itself block startup
    }

    /// <summary>Same executable image? Compares paths; if the other is unreadable (higher integrity), assume yes.</summary>
    private static bool SameImage(Process other, Process me)
    {
        try
        {
            string? mine = me.MainModule?.FileName;
            string? theirs = other.MainModule?.FileName; // throws for a higher-integrity process
            return theirs is null || mine is null || string.Equals(theirs, mine, StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }

    private const int SW_RESTORE = 9;
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private static async Task RunCameraSelfTestAsync()
    {
        var outFile = Path.Combine(Path.GetTempPath(), "simpleray_camtest.txt");
        var scanner = new WebcamQrScanner();
        int frames = 0, lastW = 0, lastH = 0;
        scanner.FrameBgra += (_, w, h) => { Interlocked.Increment(ref frames); lastW = w; lastH = h; };
        try
        {
            await scanner.StartAsync();
            for (int i = 0; i < 50 && Volatile.Read(ref frames) < 3; i++)
                await Task.Delay(100);
            File.WriteAllText(outFile, frames > 0 ? $"OK frames={frames} size={lastW}x{lastH}" : "NO_FRAMES");
        }
        catch (Exception ex)
        {
            File.WriteAllText(outFile, "ERROR: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            await scanner.DisposeAsync();
        }
    }
}
