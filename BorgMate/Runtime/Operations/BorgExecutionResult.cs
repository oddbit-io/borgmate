using BorgMate.Infrastructure.Borg;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful raw borg execution details before operation-specific result mapping.</summary>
public sealed record BorgExecutionResult(BorgResult Result);