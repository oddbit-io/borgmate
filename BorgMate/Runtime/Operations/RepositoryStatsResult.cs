using BorgMate.Domain.Repositories;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful repository statistics operation result.</summary>
public class RepositoryStatsResult
{
    public long? TotalSize { get; init; }

    public long? TotalChunks { get; init; }

    public StorageSpaceSnapshot? StorageSpace { get; init; }
}
