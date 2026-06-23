namespace BorgMate.Infrastructure.Borg;

/// <summary>Result of probing local Borg executable candidates.</summary>
internal sealed record BorgPathDetectionResult(string EffectivePath, string? DetectedPath);
