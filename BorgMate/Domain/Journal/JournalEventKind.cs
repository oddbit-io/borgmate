namespace BorgMate.Domain.Journal;

/// <summary>
///     Borg operation type. Explicit integer values are stored in the SQLite journal - do not reorder.
/// </summary>
public enum JournalEventKind
{
    Backup = 0,
    Prune = 1,
    Check = 2,
    Compact = 3,
    Delete = 4,
    Create = 5,
    PassphraseFailed = 6,
    Restore = 7,
    ChangePassphrase = 8,
    BackupDeferred = 9
}