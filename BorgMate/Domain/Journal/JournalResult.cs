namespace BorgMate.Domain.Journal;

/// <summary>Entry lifecycle: created as Running at operation start, updated to final state via Complete().</summary>
public enum JournalResult
{
    Running,
    Completed,
    Failed,
    Cancelled
}