using System;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Represents an expected or boundary-mapped failure from a runtime operation.</summary>
public abstract record OperationError
{
    /// <summary>A borg command failed and returned an exact modern exit code when one was available.</summary>
    public sealed record BorgFailure(BorgModernExitCode? Code, int ExitCode, string StderrExcerpt) : OperationError;

    /// <summary>A caller supplied invalid operation input.</summary>
    public sealed record Validation(string MessageKey) : OperationError;

    /// <summary>The operation was cancelled before completion.</summary>
    public sealed record Cancelled : OperationError;

    /// <summary>An unexpected infrastructure or programming fault reached the runtime boundary.</summary>
    public sealed record Unexpected(Exception Exception) : OperationError;
}
