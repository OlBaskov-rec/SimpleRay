using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SimpleRay.App.Services;

namespace SimpleRay.App;

public partial class App : Application
{
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
    }

    private static void ReportCrash(Exception ex)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Infrastructure.AppPaths.DataDir, "last-error.txt"),
                $"{DateTime.Now:O}{Environment.NewLine}{ex}");
        }
        catch { /* crash reporting must never throw */ }

        MessageBox.Show(
            "Непредвиденная ошибка: " + ex.Message +
            "\n\nПодробности сохранены в last-error.txt (папка данных приложения).",
            "SimpleRay", MessageBoxButton.OK, MessageBoxImage.Error);
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

        new MainWindow().Show();
    }

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
