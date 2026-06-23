namespace BorgMate.Runtime.Operations;

/// <summary>Successful SSH key preparation result.</summary>
public sealed record SshKeyPreparationResult(string OriginalKeyPath, string? PreparedKeyPath);