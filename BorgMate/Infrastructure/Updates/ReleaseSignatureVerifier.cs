using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Bcpg.Sig;

namespace BorgMate.Infrastructure.Updates;

/// <summary>
///     Verifies detached OpenPGP signatures made by a release signing subkey of one trusted key. A
///     subkey signs releases only while the primary key binds it for signing and it is neither revoked
///     nor expired; the primary key itself only certifies subkeys.
/// </summary>
public class ReleaseSignatureVerifier
{
    private const string ReleaseKeyResource = "BorgMate.Resources.Updates.release-signing-key.asc";

    // Bounds the input before the OpenPGP parser sees it; real signatures are under 2 KiB.
    private const int MaxSignatureBytes = 16 * 1024;

    private static readonly HashAlgorithmTag[] AcceptedHashAlgorithms =
        [HashAlgorithmTag.Sha256, HashAlgorithmTag.Sha384, HashAlgorithmTag.Sha512];

    // Tolerates a client clock that runs behind the release machine.
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromDays(1);

    private readonly Dictionary<long, ReleaseSigningKey> _signingKeys = new();
    private readonly TimeProvider _clock;

    /// <param name="armoredPublicKeyRing">Public key ring of the release key with its signing subkeys.</param>
    /// <param name="clock">Clock for expiration checks; the system clock when null.</param>
    /// <exception cref="InvalidDataException">The ring holds no valid signing subkey.</exception>
    public ReleaseSignatureVerifier(Stream armoredPublicKeyRing, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        var ring = new PgpPublicKeyRing(PgpUtilities.GetDecoderStream(armoredPublicKeyRing));
        var primary = ring.GetPublicKey();
        if (!IsRevoked(primary))
            foreach (var key in ring.GetPublicKeys().Where(key => !key.IsMasterKey))
                if (ReadSigningSubkey(primary, key) is { } signingKey)
                    _signingKeys.Add(key.KeyId, signingKey);

        if (_signingKeys.Count == 0)
            throw new InvalidDataException("The release key ring has no valid signing subkey.");
    }

    public static ReleaseSignatureVerifier CreateForReleases()
    {
        using var stream = typeof(ReleaseSignatureVerifier).Assembly.GetManifestResourceStream(ReleaseKeyResource)
                           ?? throw new InvalidOperationException($"Missing embedded resource {ReleaseKeyResource}.");
        return new ReleaseSignatureVerifier(stream);
    }

    /// <summary>
    ///     Accepts <paramref name="data" /> when any signature in the armored detached signature block
    ///     is a valid binary-document signature by a trusted signing subkey, made while it was valid.
    /// </summary>
    /// <exception cref="ReleaseVerificationException">No trusted signature covers the data.</exception>
    public void Verify(byte[] data, byte[] armoredSignature)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var foundTrustedSignature = false;
        foreach (var signature in ReadSignatures(armoredSignature))
        {
            if (!_signingKeys.TryGetValue(signature.KeyId, out var signingKey)
                || signature.SignatureType != PgpSignature.BinaryDocument
                || !AcceptedHashAlgorithms.Contains(signature.HashAlgorithm)
                || !signingKey.IsValidFor(signature.CreationTime, now, MaxClockSkew))
                continue;

            foundTrustedSignature = true;
            signature.InitVerify(signingKey.Key);
            signature.Update(data);
            if (signature.Verify())
                return;
        }

        throw foundTrustedSignature
            ? new ReleaseVerificationException(ReleaseVerificationFailure.InvalidSignature,
                "The trusted signature does not match the signed data.")
            : new ReleaseVerificationException(ReleaseVerificationFailure.UntrustedSignature,
                "No signature was made by a valid release signing subkey.");
    }

    /// <summary>Returns the subkey when its newest valid binding marks it for signing and it is not revoked.</summary>
    private static ReleaseSigningKey? ReadSigningSubkey(PgpPublicKey primary, PgpPublicKey subkey)
    {
        var binding = subkey.GetSignaturesOfType(PgpSignature.SubkeyBinding)
            .Where(signature => VerifiesSubkey(signature, primary, subkey))
            .MaxBy(signature => signature.CreationTime);
        var packets = binding?.GetHashedSubPackets();
        if (packets is null || (packets.GetKeyFlags() & KeyFlags.SignData) == 0)
            return null;

        if (subkey.GetSignaturesOfType(PgpSignature.SubkeyRevocation)
            .Any(signature => VerifiesSubkey(signature, primary, subkey)))
            return null;

        var validSeconds = packets.GetKeyExpirationTime();
        return new ReleaseSigningKey
        {
            Key = subkey,
            ExpiresAt = validSeconds > 0 ? subkey.CreationTime.AddSeconds(validSeconds) : null
        };
    }

    private static bool VerifiesSubkey(PgpSignature signature, PgpPublicKey primary, PgpPublicKey subkey)
    {
        signature.InitVerify(primary);
        return signature.VerifyCertification(primary, subkey);
    }

    private static bool IsRevoked(PgpPublicKey primary)
    {
        return primary.GetSignaturesOfType(PgpSignature.KeyRevocation).Any(signature =>
        {
            signature.InitVerify(primary);
            return signature.VerifyCertification(primary);
        });
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
