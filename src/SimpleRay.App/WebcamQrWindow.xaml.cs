using System;
using System.Windows;
using System.Windows.Media.Imaging;
using SimpleRay.App.Services;
using SimpleRay.Core.Profiles;

namespace SimpleRay.App;

public partial class WebcamQrWindow : Window
{
    private readonly WebcamQrScanner _scanner = new();
    private WriteableBitmap? _preview;
    private long _lastDecodeTick;
    private volatile bool _done;

    /// <summary>The decoded share-link text, once a QR with a valid profile is found.</summary>
    public string? Result { get; private set; }

    public WebcamQrWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scanner.FrameBgra += OnFrame;
        try
        {
            await _scanner.StartAsync();
            StatusText.Text = "Камера запущена…";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = "Нет доступа к камере. Разрешите доступ в «Параметры → Конфиденциальность → Камера».";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Камера недоступна: " + ex.Message;
        }
    }

    private void OnFrame(byte[] bgra, int w, int h)
    {
        if (_done) return;

        // Preview update on the UI thread.
        Dispatcher.BeginInvoke(() => UpdatePreview(bgra, w, h));

        // Decode is throttled so it doesn't saturate the CPU at frame rate.
        var now = Environment.TickCount64;
        if (now - _lastDecodeTick < 120) return;
        _lastDecodeTick = now;

        var text = QrDecoder.DecodeBgra32(bgra, w, h);
        if (string.IsNullOrWhiteSpace(text)) return;

        // Only accept a QR that actually contains a usable profile.
        if (ShareLinkParser.ParseMany(text).Count == 0) return;

        _done = true;
        Dispatcher.BeginInvoke(() =>
        {
            Result = text;
            DialogResult = true;
            Close();
        });
    }

    private void UpdatePreview(byte[] bgra, int w, int h)
    {
        if (_done) return;
        if (_preview is null || _preview.PixelWidth != w || _preview.PixelHeight != h)
        {
            _preview = new WriteableBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            Preview.Source = _preview;
        }
        _preview.WritePixels(new Int32Rect(0, 0, w, h), bgra, w * 4, 0);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _scanner.FrameBgra -= OnFrame;
        _ = _scanner.DisposeAsync(); // fire-and-forget teardown
    }
}
