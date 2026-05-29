using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace SimpleRay.App.Services;

/// <summary>
/// Decodes a QR code from a WPF <see cref="BitmapSource"/> (file or clipboard image)
/// using ZXing.Net. Uses WPF imaging only — no System.Drawing dependency.
/// </summary>
public static class QrDecoder
{
    /// <summary>Returns the decoded text, or null if no QR code was found.</summary>
    public static string? Decode(BitmapSource source)
    {
        // Normalize to BGRA32 so the byte layout matches RGBLuminanceSource.
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int w = bgra.PixelWidth;
        int h = bgra.PixelHeight;
        if (w <= 0 || h <= 0) return null;

        int stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        var luminance = new RGBLuminanceSource(pixels, w, h, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
            },
        };

        return reader.Decode(luminance)?.Text;
    }

    /// <summary>Loads an image file and decodes a QR code from it.</summary>
    public static string? DecodeFile(string path)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad; // load now, don't lock the file
        img.UriSource = new System.Uri(path);
        img.EndInit();
        return Decode(img);
    }
}
