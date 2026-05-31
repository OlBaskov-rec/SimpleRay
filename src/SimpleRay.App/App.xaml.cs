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
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
