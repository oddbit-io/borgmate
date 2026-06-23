using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for invalidating archive-related runtime caches.</summary>
public sealed record CacheInvalidationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink) : BorgOperationOptions(Repository, ProgressSink);