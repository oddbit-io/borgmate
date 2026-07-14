using System;

namespace BorgMate.Infrastructure.Platform;

/// <summary>Expected failure while querying a repository volume.</summary>
public class DiskSpaceQueryException : Exception
{
    public DiskSpaceQueryException(string message, int? exitCode = null) : base(message)
    {
        ExitCode = exitCode;
    }

    public int? ExitCode { get; }
}
