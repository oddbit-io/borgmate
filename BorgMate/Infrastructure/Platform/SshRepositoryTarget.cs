namespace BorgMate.Infrastructure.Platform;

/// <summary>Resolved SSH destination and repository path.</summary>
public class SshRepositoryTarget
{
    public required string Host { get; init; }

    public required string RepositoryPath { get; init; }

    public int Port { get; init; } = 22;
}
