using System.Threading;
using System.Threading.Tasks;
using BorgMate.Runtime.Orchestration;
using OneOf;

namespace BorgMate.Runtime.Operations;

/// <summary>Runs archive and repository query operations through the runtime lifecycle.</summary>
public interface IArchiveOperationRunner
{
    /// <summary>Lists repository archives.</summary>
    Task<OneOf<ArchiveListResult, OperationError>> ListArchivesAsync(
        ArchiveListOperationOptions options, CancellationToken ct = default);

    /// <summary>Loads detail for a single archive.</summary>
    Task<OneOf<ArchiveDetailResult, OperationError>> GetArchiveDetailAsync(
        ArchiveDetailOperationOptions options, CancellationToken ct = default);

    /// <summary>Loads repository aggregate statistics.</summary>
    Task<OneOf<RepositoryStatsResult, OperationError>> GetRepositoryStatsAsync(
        RepositoryStatsOperationOptions options, CancellationToken ct = default);

    /// <summary>Lists archive contents.</summary>
    Task<OneOf<ArchiveContentsResult, OperationError>> ListArchiveContentsAsync(
        ArchiveContentsOperationOptions options, CancellationToken ct = default);

    /// <summary>Compares two archives.</summary>
    Task<OneOf<ArchiveDiffResult, OperationError>> GetArchiveDiffAsync(
        ArchiveDiffOperationOptions options, CancellationToken ct = default);

    /// <summary>Invalidates cache entries for a repository.</summary>
    void InvalidateRepositoryCache(CacheInvalidationOptions options);
}