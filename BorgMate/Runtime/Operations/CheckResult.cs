using System;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful repository check operation result.</summary>
public sealed record CheckResult(DateTimeOffset CompletedAt);