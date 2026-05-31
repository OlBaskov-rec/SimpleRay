using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;

namespace SimpleRay.App.Services;

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

/// <summary>
/// Live camera frame source via WinRT MediaCapture (no external native packages).
/// Raises <see cref="FrameBgra"/> per frame with tightly-packed BGRA8 pixels for
/// preview + QR decoding. Frames arrive on a thread-pool thread.
/// </summary>
public sealed class WebcamQrScanner : IAsyncDisposable
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;

    /// <summary>(bgra bytes, width, height) — invoked on a thread-pool thread.</summary>
    public event Action<byte[], int, int>? FrameBgra;

    public async Task StartAsync()
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        MediaFrameSourceGroup? group = null;
        MediaFrameSourceInfo? colorInfo = null;
        foreach (var g in groups)
        {
            var info = g.SourceInfos.FirstOrDefault(si =>
                si.SourceKind == MediaFrameSourceKind.Color &&
                (si.MediaStreamType == MediaStreamType.VideoPreview ||
                 si.MediaStreamType == MediaStreamType.VideoRecord));
            if (info is not null) { group = g; colorInfo = info; break; }
        }
        if (group is null || colorInfo is null)
            throw new InvalidOperationException("Камера не найдена.");

        _capture = new MediaCapture();
        await _capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            SourceGroup = group,
            SharingMode = MediaCaptureSharingMode.ExclusiveControl,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            StreamingCaptureMode = StreamingCaptureMode.Video,
        });

        // Read the camera's native format and convert each frame to BGRA8 ourselves —
        // more compatible than forcing a subtype the device may not deliver.
        var source = _capture.FrameSources[colorInfo.Id];
        _reader = await _capture.CreateFrameReaderAsync(source);
        _reader.FrameArrived += OnFrameArrived;
        await _reader.StartAsync();
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var frame = sender.TryAcquireLatestFrame();
        var bmp = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bmp is null) return;

        SoftwareBitmap? converted = null;
        try
        {
            var src = bmp;
            if (bmp.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
            {
                converted = SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                src = converted;
            }

            var bytes = CopyBgra(src);
            if (bytes is not null)
                FrameBgra?.Invoke(bytes, src.PixelWidth, src.PixelHeight);
        }
        catch
        {
            // drop the frame; the next one will come along
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private static unsafe byte[]? CopyBgra(SoftwareBitmap bmp)
    {
        using var buffer = bmp.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        var byteAccess = (IMemoryBufferByteAccess)reference;
        byteAccess.GetBuffer(out byte* dataPtr, out uint capacity);
        if (capacity == 0) return null;

        var plane = buffer.GetPlaneDescription(0);
        int w = plane.Width, h = plane.Height, stride = plane.Stride, start = plane.StartIndex;

        // Repack into a tight w*4 stride (the source stride may include row padding).
        var dst = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Marshal.Copy((IntPtr)(dataPtr + start + y * stride), dst, y * w * 4, w * 4);
        return dst;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_reader is not null)
            {
                _reader.FrameArrived -= OnFrameArrived;
                await _reader.StopAsync();
                _reader.Dispose();
            }
        }
        catch { /* ignore teardown errors */ }
        finally
        {
            _capture?.Dispose();
            _reader = null;
            _capture = null;
        }
    }
}
