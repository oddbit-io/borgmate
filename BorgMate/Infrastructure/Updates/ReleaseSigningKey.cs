using System;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace BorgMate.Infrastructure.Updates;

/// <summary>A release signing subkey and the end of the validity its binding signature grants.</summary>
public class ReleaseSigningKey
{
    public required PgpPublicKey Key { get; init; }

    /// <summary>Null when the binding sets no expiration.</summary>
    public required DateTime? ExpiresAt { get; init; }

    /// <summary>True when a signature made at <paramref name="signedAt" /> is acceptable at <paramref name="now" />.</summary>
    public bool IsValidFor(DateTime signedAt, DateTime now, TimeSpan clockSkew)
    {
        return signedAt >= Key.CreationTime && signedAt <= now + clockSkew &&
               (ExpiresAt is not { } expiresAt || (signedAt < expiresAt && now < expiresAt));
    }
}
