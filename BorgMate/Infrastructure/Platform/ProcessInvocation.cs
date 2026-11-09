using System.Collections.Generic;

namespace BorgMate.Infrastructure.Platform;

/// <summary>A program and its argv elements; no shell parses the arguments.</summary>
public record ProcessInvocation
{
    public required string FileName { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }
}
