namespace BorgMate.Infrastructure.Platform;

/// <summary>ssh-add accepted the key; stderr carries its diagnostic output.</summary>
public sealed record SshKeyAdded(string Stderr);
