using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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

    // One reusable frame buffer instead of ~8 MB per frame: reallocated only when the frame
    // size changes. Overlapping FrameArrived callbacks are dropped (TryEnter) so a frame is
    // never overwritten while a handler is still reading it.
    private readonly object _frameGate = new();
    private byte[]? _buffer;

    /// <summary>
    /// (bgra bytes, width, height) — invoked on a thread-pool thread. The buffer is reused
    /// across frames, so it is valid ONLY for the duration of the synchronous call; a handler
    /// that needs to keep the pixels must copy them before returning.
    /// </summary>
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
        // Drop this frame if a previous one is still being processed: the shared buffer must
        // not be overwritten while a handler reads it. The camera just delivers the next frame.
        if (!Monitor.TryEnter(_frameGate)) return;
        try
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

                int w = src.PixelWidth, h = src.PixelHeight;
                var buffer = EnsureBuffer(w, h);
                if (CopyBgraInto(src, buffer))
                    FrameBgra?.Invoke(buffer, w, h);
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
        finally
        {
            Monitor.Exit(_frameGate);
        }
    }

    /// <summary>Returns the reusable buffer, (re)allocating it only when the frame size changes.</summary>
    private byte[] EnsureBuffer(int w, int h)
    {
        int needed = w * h * 4;
        if (_buffer is null || _buffer.Length != needed)
            _buffer = new byte[needed];
        return _buffer;
    }

    private static unsafe bool CopyBgraInto(SoftwareBitmap bmp, byte[] dst)
    {
        using var buffer = bmp.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        var byteAccess = (IMemoryBufferByteAccess)reference;
        byteAccess.GetBuffer(out byte* dataPtr, out uint capacity);
        if (capacity == 0) return false;

        var plane = buffer.GetPlaneDescription(0);
        int w = plane.Width, h = plane.Height, stride = plane.Stride, start = plane.StartIndex;
        if (dst.Length < w * h * 4) return false;

        // Repack into a tight w*4 stride (the source stride may include row padding).
        for (int y = 0; y < h; y++)
            Marshal.Copy((IntPtr)(dataPtr + start + y * stride), dst, y * w * 4, w * 4);
        return true;
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
