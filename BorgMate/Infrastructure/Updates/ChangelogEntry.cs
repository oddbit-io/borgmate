namespace BorgMate.Infrastructure.Updates;

public class ChangelogEntry
{
    public required string Version { get; init; }

    public string? Date { get; init; }

    public required ChangelogChanges Changes { get; init; }
}
