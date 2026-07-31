using System.Security.Cryptography;

namespace SimpleRay.Core.Update;

/// <summary>
/// Verifies a release artifact against the embedded release-signing public key
/// (ECDSA P-256 over SHA-256, DER signature — what <c>openssl dgst -sha256 -sign</c>
/// produces). A SHA-256 sidecar only proves the download wasn't corrupted; a signature
/// proves it came from whoever holds the private key, so a compromised GitHub release
/// cannot forge an accepted update.
/// </summary>
public static class UpdateSignature
{
    /// <summary>
    /// Base64 of the signing public key in SubjectPublicKeyInfo (DER) form. Generate it
    /// with <c>scripts/sign-release.ps1 -GenerateKey</c> and paste the printed value here.
    /// Empty means "not configured" — the updater then falls back to the SHA-256 sidecar;
    /// once this is set, a valid signature becomes mandatory.
    /// </summary>
    public const string PublicKeyBase64 = "";

    /// <summary>True once a signing public key has been embedded.</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKeyBase64);

    /// <summary>Verifies <paramref name="signature"/> over <paramref name="data"/> with the embedded key.</summary>
    public static bool Verify(Stream data, byte[] signature) => Verify(data, signature, PublicKeyBase64);

    public static bool Verify(Stream data, byte[] signature, string publicKeyBase64)
    {
        if (data is null || signature is null || signature.Length == 0 ||
            string.IsNullOrWhiteSpace(publicKeyBase64))
            return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false; // malformed key or signature — reject, don't crash
        }
    }

    /// <summary>Byte-array convenience overload (used by tests).</summary>
    public static bool Verify(byte[] data, byte[] signature, string publicKeyBase64)
    {
        using var ms = new MemoryStream(data);
        return Verify(ms, signature, publicKeyBase64);
    }
}
