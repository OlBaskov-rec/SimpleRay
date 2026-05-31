using System;
using SimpleRay.Core.Update;
using Xunit;

namespace SimpleRay.Core.Tests;

public class ReleaseParserTests
{
    private static string Release(string tag, bool withSha = true)
    {
        var sha = withSha
            ? """, { "name": "SimpleRay-X-win-x64.zip.sha256", "browser_download_url": "https://h/zip.sha256" }"""
            : "";
        return $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://h/releases/tag/{{tag}}",
          "body": "notes here",
          "assets": [
            { "name": "SimpleRay-X-win-x64.zip", "browser_download_url": "https://h/zip" }{{sha}}
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
    public void MissingSha256Asset_IsRejected()
    {
        Assert.False(ReleaseParser.TryParseLatest(Release("v0.2.0", withSha: false), new Version(0, 1, 0, 0), out _));
    }

    [Fact]
    public void Garbage_IsRejected()
    {
        Assert.False(ReleaseParser.TryParseLatest("not json", new Version(0, 1, 0, 0), out _));
    }
}
