using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace BorgMate.Infrastructure.Updates;

/// <summary>
///     Verifies detached OpenPGP signatures made by any key of one trusted public key ring.
/// </summary>
public class ReleaseSignatureVerifier
{
    private const string ReleaseKeyResource = "BorgMate.Resources.Updates.release-signing-key.asc";

    // Bounds the input before the OpenPGP parser sees it; real signatures are under 2 KiB.
    private const int MaxSignatureBytes = 16 * 1024;

    private static readonly HashAlgorithmTag[] AcceptedHashAlgorithms =
        [HashAlgorithmTag.Sha256, HashAlgorithmTag.Sha384, HashAlgorithmTag.Sha512];

    private readonly Dictionary<long, PgpPublicKey> _trustedKeys = new();

    /// <param name="armoredPublicKeyRing">Public key ring whose keys are all trusted to sign.</param>
    public ReleaseSignatureVerifier(Stream armoredPublicKeyRing)
    {
        var ring = new PgpPublicKeyRing(PgpUtilities.GetDecoderStream(armoredPublicKeyRing));
        foreach (var key in ring.GetPublicKeys())
            _trustedKeys.Add(key.KeyId, key);
    }

    public static ReleaseSignatureVerifier CreateForReleases()
    {
        using var stream = typeof(ReleaseSignatureVerifier).Assembly.GetManifestResourceStream(ReleaseKeyResource)
                           ?? throw new InvalidOperationException($"Missing embedded resource {ReleaseKeyResource}.");
        return new ReleaseSignatureVerifier(stream);
    }

    /// <summary>
    ///     Accepts <paramref name="data" /> when any signature in the armored detached signature block
    ///     is a valid binary-document signature by a trusted key.
    /// </summary>
    /// <exception cref="ReleaseVerificationException">No trusted signature covers the data.</exception>
    public void Verify(byte[] data, byte[] armoredSignature)
    {
        var foundTrustedSignature = false;
        foreach (var signature in ReadSignatures(armoredSignature))
        {
            if (!_trustedKeys.TryGetValue(signature.KeyId, out var key)
                || signature.SignatureType != PgpSignature.BinaryDocument
                || !AcceptedHashAlgorithms.Contains(signature.HashAlgorithm))
                continue;

            foundTrustedSignature = true;
            signature.InitVerify(key);
            signature.Update(data);
            if (signature.Verify())
                return;
        }

        throw foundTrustedSignature
            ? new ReleaseVerificationException(ReleaseVerificationFailure.InvalidSignature,
                "The trusted signature does not match the signed data.")
            : new ReleaseVerificationException(ReleaseVerificationFailure.UntrustedSignature,
                "No signature was made by a trusted release signing key.");
    }

    private static List<PgpSignature> ReadSignatures(byte[] armoredSignature)
    {
        if (armoredSignature.Length > MaxSignatureBytes)
            throw new ReleaseVerificationException(ReleaseVerificationFailure.InvalidSignature,
                $"The signature is larger than {MaxSignatureBytes} bytes.");

        try
        {
            using var input = PgpUtilities.GetDecoderStream(new MemoryStream(armoredSignature));
            if (new PgpObjectFactory(input).NextPgpObject() is not PgpSignatureList signatures)
                throw new ReleaseVerificationException(ReleaseVerificationFailure.InvalidSignature,
                    "The signature file holds no signature packets.");

            var result = new List<PgpSignature>();
            for (var i = 0; i < signatures.Count; i++)
                result.Add(signatures[i]);
            return result;
        }
        catch (Exception ex) when (ex is IOException or PgpException)
        {
            throw new ReleaseVerificationException(ReleaseVerificationFailure.InvalidSignature,
                "The signature file is not a valid OpenPGP signature.", ex);
        }
    }
}
