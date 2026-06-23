namespace BorgMate.Runtime.Operations;

/// <summary>Successful archive detail operation result.</summary>
public sealed record ArchiveDetailResult(long OriginalSize, long FileCount);