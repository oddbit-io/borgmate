namespace BorgMate.Infrastructure.Platform;

/// <summary>Represents a failed ssh-add attempt.</summary>
public abstract record SshAddError
{
    /// <summary>ssh-add rejected the supplied passphrase.</summary>
    public sealed record WrongPassphrase(string Stderr) : SshAddError;

    /// <summary>ssh-add failed for another reason.</summary>
    public sealed record Failed(string Stderr) : SshAddError;
}
