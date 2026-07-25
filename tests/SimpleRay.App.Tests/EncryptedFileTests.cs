using System;
using System.IO;
using System.Text;
using SimpleRay.App.Infrastructure;

namespace SimpleRay.App.Tests;

public class EncryptedFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sraytest_" + Guid.NewGuid().ToString("N"));

    public EncryptedFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void RoundTrip_DecryptsBack()
    {
        var path = Path.Combine(_dir, "p.json");
        const string plain = "{\"secret\":\"пароль-42\"}";
        EncryptedFile.WriteAllText(path, plain);
        Assert.Equal(plain, EncryptedFile.ReadAllText(path));
    }

    [Fact]
    public void OnDisk_IsNotPlaintext()
    {
        var path = Path.Combine(_dir, "q.json");
        const string plain = "sensitive-uuid-and-password";
        EncryptedFile.WriteAllText(path, plain);

        var raw = File.ReadAllBytes(path);
        // DPAPI blobs start with 0x01 0x00 0x00 0x00 and never contain the plaintext.
        Assert.DoesNotContain(plain, Encoding.UTF8.GetString(raw));
        Assert.True(raw.Length >= 4 && raw[0] == 0x01);
    }

    [Fact]
    public void LegacyPlaintext_IsReadAsIs()
    {
        var path = Path.Combine(_dir, "legacy.json");
        const string plain = "[{\"Tag\":\"old\"}]";
        File.WriteAllText(path, plain); // unencrypted, as older builds wrote it
        Assert.Equal(plain, EncryptedFile.ReadAllText(path));
    }
}
