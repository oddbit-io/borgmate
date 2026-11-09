namespace BorgMate.Tests.Infrastructure;

internal record BashRunResult
{
    public required int ExitCode { get; init; }

    public required string Stdout { get; init; }
}
