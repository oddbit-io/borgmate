namespace BorgMate.Infrastructure.Platform;

/// <summary>Represents a failed SMB mount attempt.</summary>
public abstract record MountError
{
    /// <summary>The server rejected the supplied or guest credentials.</summary>
    public sealed record AuthRejected : MountError;

    /// <summary>The mount failed for another reason; the message is localized and user-facing.</summary>
    public sealed record Failed(string Message) : MountError;
}
