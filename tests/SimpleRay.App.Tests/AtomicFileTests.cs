using System;
using System.IO;
using System.Text;
using SimpleRay.App.Infrastructure;

namespace SimpleRay.App.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sraytest_" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void WriteAllText_CreatesFile()
    {
        var path = Path.Combine(_dir, "a.txt");
        AtomicFile.WriteAllText(path, "hello");
        Assert.Equal("hello", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp")); // temp swapped away
    }

    [Fact]
    public void WriteAllText_ReplacesExisting()
    {
        var path = Path.Combine(_dir, "b.txt");
        AtomicFile.WriteAllText(path, "first");
        AtomicFile.WriteAllText(path, "second");
        Assert.Equal("second", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAllBytes_RoundTrips()
    {
        var path = Path.Combine(_dir, "c.bin");
        var bytes = Encoding.UTF8.GetBytes("binary content");
        AtomicFile.WriteAllBytes(path, bytes);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
