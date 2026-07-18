using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SimpleRay.App.Infrastructure;

/// <summary>
/// DPAPI-at-rest text storage (CurrentUser scope): stored credentials are encrypted
/// so only the current Windows user on this machine can read them. Writes are atomic.
/// Load tolerates a legacy plaintext file and lets the caller re-save it encrypted.
/// </summary>
public static class EncryptedFile
{
    // Extra entropy binds the ciphertext to this app's purpose.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SimpleRay.profiles.v1");

    public static void WriteAllText(string path, string contents)
    {
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(contents), Entropy, DataProtectionScope.CurrentUser);
        AtomicFile.WriteAllBytes(path, cipher);
    }

    /// <summary>Reads and decrypts; falls back to treating the file as legacy plaintext.</summary>
    public static string ReadAllText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Legacy unencrypted file (plain JSON) — read as-is; the next save encrypts it.
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
