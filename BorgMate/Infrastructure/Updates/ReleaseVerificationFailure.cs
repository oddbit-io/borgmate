namespace BorgMate.Infrastructure.Updates;

public enum ReleaseVerificationFailure
{
    /// <summary>The server has no manifest or signature.</summary>
    SignatureMissing,
    InvalidSignature,
    UntrustedSignature,
    InvalidManifest,
    ManifestMismatch,
    FeedHashMismatch
}
