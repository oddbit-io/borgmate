using System;

namespace BorgMate.Infrastructure.Updates;

/// <summary>The update server returned a release that is not provably signed for this installation.</summary>
public class ReleaseVerificationException : Exception
{
    public ReleaseVerificationException(ReleaseVerificationFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public ReleaseVerificationFailure Failure { get; }
}
