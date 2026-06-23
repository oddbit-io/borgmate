using System;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful compact operation result.</summary>
public sealed record CompactResult(DateTimeOffset CompletedAt);