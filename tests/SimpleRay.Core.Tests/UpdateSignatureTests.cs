using System.Security.Cryptography;
using System.Text;
using SimpleRay.Core.Update;
using Xunit;

namespace SimpleRay.Core.Tests;

public class UpdateSignatureTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("pretend this is a release zip");

    /// <summary>Generates an ephemeral P-256 key; returns (publicKeyBase64, signer).</summary>
    private static (string pub, ECDsa key) NewKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = System.Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        return (pub, key);
    }

    private static byte[] Sign(ECDsa key, byte[] data) =>
        key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    [Fact]
    public void ValidSignature_Verifies()
    {
        var (pub, key) = NewKey();
        var sig = Sign(key, Payload);

        Assert.True(UpdateSignature.Verify(Payload, sig, pub));
    }

    [Fact]
    public void TamperedData_FailsVerification()
    {
        var (pub, key) = NewKey();
        var sig = Sign(key, Payload);

        var tampered = (byte[])Payload.Clone();
        tampered[0] ^= 0xFF;

        Assert.False(UpdateSignature.Verify(tampered, sig, pub));
    }

    [Fact]
    public void SignatureFromAnotherKey_FailsVerification()
    {
        var (_, signer) = NewKey();
        var (otherPub, _) = NewKey();
        var sig = Sign(signer, Payload);

        // Signed by one key, checked against another — must fail (this is the whole point:
        // an attacker without the private key cannot forge an accepted update).
        Assert.False(UpdateSignature.Verify(Payload, sig, otherPub));
    }

    [Fact]
    public void EmptyOrGarbageInputs_FailClosed()
    {
        var (pub, key) = NewKey();
        var sig = Sign(key, Payload);

        Assert.False(UpdateSignature.Verify(Payload, sig, ""));                 // no key configured
        Assert.False(UpdateSignature.Verify(Payload, System.Array.Empty<byte>(), pub)); // no signature
        Assert.False(UpdateSignature.Verify(Payload, new byte[] { 1, 2, 3 }, pub));     // malformed signature
        Assert.False(UpdateSignature.Verify(Payload, sig, "not-base64-@@@"));   // malformed key
    }

    [Fact]
    public void IsConfigured_ReflectsEmbeddedKey()
    {
        // Ships unconfigured; enabling signing is embedding the public key.
        Assert.Equal(!string.IsNullOrWhiteSpace(UpdateSignature.PublicKeyBase64), UpdateSignature.IsConfigured);
    }
}
