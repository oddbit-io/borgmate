namespace BorgMate.Runtime.Operations;

/// <summary>Successful repository statistics operation result.</summary>
public sealed record RepositoryStatsResult(long? TotalSize, long? TotalChunks);