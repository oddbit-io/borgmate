using System;
using System.Collections.Generic;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Maps Borg modern process exit codes to exact runtime error codes and policy groups.</summary>
public static class BorgModernExitCodeMapper
{
    private static readonly BorgModernExitCode[] WrongPassphraseCodes =
    [
        BorgModernExitCode.NoPassphraseFailure,
        BorgModernExitCode.PasscommandFailure,
        BorgModernExitCode.PassphraseWrong,
        BorgModernExitCode.PasswordRetriesExceeded
    ];

    private static readonly BorgModernExitCode[] RetryableCodes =
    [
        BorgModernExitCode.LockError,
        BorgModernExitCode.LockErrorT,
        BorgModernExitCode.LockFailed,
        BorgModernExitCode.LockTimeout,
        BorgModernExitCode.NotLocked,
        BorgModernExitCode.NotMyLock,
        BorgModernExitCode.ConnectionClosed,
        BorgModernExitCode.ConnectionClosedWithHint,
        BorgModernExitCode.ConnectionBrokenWithHint
    ];

    /// <summary>Returns the exact Borg error code for a process exit code, or null for non-Borg values.</summary>
    public static BorgModernExitCode? TryMap(int exitCode)
    {
        return Enum.IsDefined(typeof(BorgModernExitCode), exitCode)
            ? (BorgModernExitCode)exitCode
            : null;
    }

    /// <summary>Returns true when the exact Borg code represents a passphrase failure.</summary>
    public static bool IsWrongPassphrase(BorgModernExitCode? code)
    {
        return code is { } value && WrongPassphraseCodes.Contains(value);
    }

    /// <summary>Returns true when the exact Borg code should be retried with transient backoff.</summary>
    public static bool IsRetryable(BorgModernExitCode? code)
    {
        return code is { } value && RetryableCodes.Contains(value);
    }
}
