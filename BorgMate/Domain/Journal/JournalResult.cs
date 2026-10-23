namespace BorgMate.Domain.Journal;

/// <summary>Entry lifecycle: created as Running at operation start, updated to final state via Complete().</summary>
public enum JournalResult
{
    Running,
    Completed,
    Failed,
    Cancelled,

    /// <summary>Postponed (e.g. on battery with AC-only backups); will run under missed-run rules.</summary>
    Deferred,

    /// <summary>Borg finished the operation but reported warnings, such as unreadable source files.</summary>
    CompletedWithWarnings
}