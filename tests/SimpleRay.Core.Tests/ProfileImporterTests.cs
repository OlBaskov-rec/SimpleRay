using System;
using System.Linq;
using SimpleRay.Core.Models;
using SimpleRay.Core.Profiles;
using Xunit;

namespace SimpleRay.Core.Tests;

public class ProfileImporterTests
{
    private const string Link1 = "vless://11111111-1111-1111-1111-111111111111@a.example.com:443?security=tls#one";
    private const string Link2 = "vless://22222222-2222-2222-2222-222222222222@b.example.com:443?security=tls#two";

    [Fact]
    public void Import_ParsesNewLinks()
    {
        var r = ProfileImporter.Import(Link1 + "\n" + Link2, Array.Empty<ProfileConfig>());

        Assert.True(r.AnyParsed);
        Assert.Equal(2, r.New.Count);
    }

    [Fact]
    public void Import_DedupsAgainstExisting()
    {
        var existing = ShareLinkParser.ParseMany(Link1); // an existing profile for Link1

        var r = ProfileImporter.Import(Link1 + "\n" + Link2, existing);

        Assert.True(r.AnyParsed);        // something parsed...
        Assert.Single(r.New);            // ...but only the not-yet-present one is returned
        Assert.Equal("b.example.com", r.New[0].Server);
    }

    [Fact]
    public void Import_DedupsWithinTheSameBatch()
    {
        var r = ProfileImporter.Import(Link1 + "\n" + Link1, Array.Empty<ProfileConfig>());

        Assert.Single(r.New); // two identical links add one
    }

    [Fact]
    public void Import_NoRecognizedLinks_AnyParsedFalse()
    {
        var r = ProfileImporter.Import("just some text, not a link", Array.Empty<ProfileConfig>());

        Assert.False(r.AnyParsed);
        Assert.Empty(r.New);
    }
}
