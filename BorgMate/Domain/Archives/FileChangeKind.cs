namespace BorgMate.Domain.Archives;

/// <summary>Change status from borg diff comparison against the previous archive.</summary>
public enum FileChangeKind
{
    None,
    Added,
    Modified
}