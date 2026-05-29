using System.Collections.Generic;
using SimpleRay.Core.Profiles;
using Xunit;
using ZXing;
using ZXing.Common;

namespace SimpleRay.Core.Tests;

/// <summary>
/// Verifies the QR decode pipeline the app uses: a share-link encoded as a QR
/// is decoded back with the same reader configuration as App.Services.QrDecoder
/// (BGRA32 luminance, TryHarder, QR_CODE, auto-rotate), then parsed to a profile.
/// </summary>
public class QrRoundTripTests
{
    [Fact]
    public void EncodedLink_DecodesAndParses()
    {
        const string link =
            "vless://b831381d-6324-4d53-ad4f-8cda48b30811@example.com:443" +
            "?security=reality&sni=www.microsoft.com&fp=chrome" +
            "&pbk=66dK2tcRJ1R6fc4cbukmnRBZPZh6tLMcRR58KCLt6AU&sid=ab12" +
            "&type=tcp&flow=xtls-rprx-vision#qr-test";

        // Encode to BGRA32 pixels (matches what QrDecoder feeds ZXing).
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 320, Height = 320, Margin = 2 },
        };
        var pixelData = writer.Write(link); // BGRA, 4 bytes per pixel

        var luminance = new RGBLuminanceSource(
            pixelData.Pixels, pixelData.Width, pixelData.Height,
            RGBLuminanceSource.BitmapFormat.BGRA32);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
            },
        };

        var decoded = reader.Decode(luminance);
        Assert.NotNull(decoded);
        Assert.Equal(link, decoded!.Text);

        // And the decoded text parses into a usable profile.
        Assert.True(ShareLinkParser.TryParse(decoded.Text, out var profile));
        Assert.NotNull(profile);
        Assert.Equal("example.com", profile!.Server);
        Assert.Equal(443, profile.Port);
    }
}
