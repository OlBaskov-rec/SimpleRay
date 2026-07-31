using System;
using SimpleRay.Core.Update;
using Xunit;

namespace SimpleRay.Core.Tests;

public class ReleaseParserTests
{
    private static string Release(string tag, bool withSha = true, bool withSig = false)
    {
        var sha = withSha
            ? """, { "name": "SimpleRay-X-win-x64.zip.sha256", "browser_download_url": "https://h/zip.sha256" }"""
            : "";
        var sig = withSig
            ? """, { "name": "SimpleRay-X-win-x64.zip.sig", "browser_download_url": "https://h/zip.sig" }"""
            : "";
        return $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://h/releases/tag/{{tag}}",
          "body": "notes here",
          "assets": [
            { "name": "SimpleRay-X-win-x64.zip", "browser_download_url": "https://h/zip" }{{sha}}{{sig}}
          ]
        }
        """;
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, 0)]
    [InlineData("1.2", 1, 2, 0, 0)]
    [InlineData("0.2.0-beta", 0, 2, 0, 0)]
    [InlineData("2.0.1.5", 2, 0, 1, 5)]
    public void TryParseVersion_Normalizes(string tag, int a, int b, int c, int d)
    {
        Assert.True(ReleaseParser.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(a, b, c, d), v);
    }

    [Fact]
    public void NewerRelease_WithAssets_IsSelected()
    {
        var ok = ReleaseParser.TryParseLatest(Release("v0.2.0"), new Version(0, 1, 0, 0), out var info);
        Assert.True(ok);
        Assert.Equal(new Version(0, 2, 0, 0), info!.Version);
        Assert.Equal("https://h/zip", info.ZipUrl);
        Assert.Equal("https://h/zip.sha256", info.Sha256Url);
        Assert.Equal("notes here", info.Notes);
    }

    [Fact]
    public void SameOrOlderVersion_IsIgnored()
    {
        Assert.False(ReleaseParser.TryParseLatest(Release("0.1.0"), new Version(0, 1, 0, 0), out _));
        Assert.False(ReleaseParser.TryParseLatest(Release("v0.0.9"), new Version(0, 1, 0, 0), out _));
    }

    [Fact]
    public void ZipWithNoVerificationSidecar_IsRejected()
    {
        // Zip alone (no .sig and no .sha256) can't be verified — fail safe.
        Assert.False(ReleaseParser.TryParseLatest(
            Release("v0.2.0", withSha: false, withSig: false), new Version(0, 1, 0, 0), out _));
    }

    [Fact]
    public void SignatureAsset_IsParsed()
    {
        var ok = ReleaseParser.TryParseLatest(
            Release("v0.3.0", withSha: true, withSig: true), new Version(0, 2, 0, 0), out var info);
        Assert.True(ok);
        Assert.Equal("https://h/zip.sig", info!.SigUrl);
        Assert.Equal("https://h/zip.sha256", info.Sha256Url);
    }

    [Fact]
    public void SignatureOnly_NoSha256_IsStillParsed()
    {
        var ok = ReleaseParser.TryParseLatest(
            Release("v0.3.0", withSha: false, withSig: true), new Version(0, 2, 0, 0), out var info);
        Assert.True(ok);
        Assert.Equal("https://h/zip.sig", info!.SigUrl);
        Assert.Null(info.Sha256Url);
    }

    [Fact]
    public void Garbage_IsRejected()
    {
        Assert.False(ReleaseParser.TryParseLatest("not json", new Version(0, 1, 0, 0), out _));
    }
}
