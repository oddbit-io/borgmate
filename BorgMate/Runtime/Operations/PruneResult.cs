using System;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful prune operation result.</summary>
public sealed record PruneResult(DateTimeOffset CompletedAt, bool ShouldCompact);