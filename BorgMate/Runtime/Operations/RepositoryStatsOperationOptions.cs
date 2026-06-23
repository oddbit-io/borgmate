using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for loading repository statistics.</summary>
public sealed record RepositoryStatsOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink) : BorgOperationOptions(Repository, ProgressSink);