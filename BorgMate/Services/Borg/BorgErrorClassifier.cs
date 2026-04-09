namespace BorgMate.Services.Borg;

public static class BorgErrorClassifier
{
    public static bool IsWrongPassphrase(BorgErrorType errorType) =>
        errorType == BorgErrorType.WrongPassphrase;

    /// <summary>
    /// Returns true for transient SSH errors: connection reset, timeout, refused, no route, broken pipe.
    /// </summary>
    public static bool IsTransientSshError(BorgErrorType errorType) =>
        errorType is BorgErrorType.SshConnectionReset
                  or BorgErrorType.SshConnectionTimedOut
                  or BorgErrorType.SshConnectionRefused
                  or BorgErrorType.SshNoRouteToHost;

    /// <summary>
    /// Returns true for errors that should be retried with backoff.
    /// Includes transient SSH errors and repository lock errors (stale locks after SSH drops).
    /// </summary>
    public static bool IsRetryable(BorgErrorType errorType) =>
        IsTransientSshError(errorType) || errorType == BorgErrorType.RepositoryLocked;

    /// <summary>
    /// Classifies stderr text (expected in English via LC_ALL=C) into an error type.
    /// </summary>
    public static BorgErrorType Classify(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return BorgErrorType.Unknown;

        var lower = stderr.ToLowerInvariant();

        // SSH errors
        if (lower.Contains("host key verification failed"))
            return BorgErrorType.SshHostKeyVerificationFailed;

        if (lower.Contains("permission denied"))
            return BorgErrorType.SshPermissionDenied;

        if (lower.Contains("connection refused"))
            return BorgErrorType.SshConnectionRefused;

        if (lower.Contains("connection timed out") || lower.Contains("operation timed out"))
            return BorgErrorType.SshConnectionTimedOut;

        if (lower.Contains("no route to host"))
            return BorgErrorType.SshNoRouteToHost;

        if (lower.Contains("could not resolve hostname") || lower.Contains("name or service not known"))
            return BorgErrorType.SshHostnameNotFound;

        if (lower.Contains("connection closed by remote host") || lower.Contains("connection reset by peer")
            || lower.Contains("broken pipe"))
            return BorgErrorType.SshConnectionReset;

        if (lower.Contains("no such file or directory") && lower.Contains("ssh"))
            return BorgErrorType.SshKeyFileNotFound;

        // Borg errors
        if (lower.Contains("repository") && lower.Contains("does not exist"))
            return BorgErrorType.RepositoryNotFound;

        if (lower.Contains("repository") && lower.Contains("already exists"))
            return BorgErrorType.RepositoryAlreadyExists;

        if (lower.Contains("passphrase") || lower.Contains("wrong passphrase") ||
            lower.Contains("incorrect passphrase") || lower.Contains("decryption failed"))
            return BorgErrorType.WrongPassphrase;

        if (lower.Contains("lock") && lower.Contains("exclusive"))
            return BorgErrorType.RepositoryLocked;

        if (lower.Contains("not a valid repository") || lower.Contains("invalid repository"))
            return BorgErrorType.InvalidRepository;

        if (lower.Contains("incompatible repository"))
            return BorgErrorType.IncompatibleRepository;

        if (lower.Contains("no space left on device"))
            return BorgErrorType.NoSpaceLeftOnDevice;

        if ((lower.Contains("command not found") || lower.Contains("no such file or directory")) &&
            lower.Contains("borg"))
            return BorgErrorType.BorgBinaryNotFound;

        return BorgErrorType.Unknown;
    }
}
