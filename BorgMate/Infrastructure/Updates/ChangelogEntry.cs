using System;

namespace BorgMate.Infrastructure.Updates;

public class ChangelogEntry
{
    public required string Version { get; init; }

    public DateOnly? Date { get; init; }

    public required ChangelogChanges Changes { get; init; }
}
