using BorgMate.Infrastructure.Borg;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful borg version detection result.</summary>
public sealed record VersionDetectionResult(string VersionText, BorgCliVersion? CliVersion = null);
