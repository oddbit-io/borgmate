namespace BorgMate.Infrastructure.Platform;

/// <summary>Successful mount attempt: where the share is mounted.</summary>
public sealed record MountedShare(string MountPoint);
