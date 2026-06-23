using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for clearing presentation-visible repository error state.</summary>
public sealed record ClearRepositoryErrorOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink) : BorgOperationOptions(Repository, ProgressSink);