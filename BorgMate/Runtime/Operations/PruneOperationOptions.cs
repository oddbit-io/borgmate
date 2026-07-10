using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for pruning a repository according to its retention rules.</summary>
public sealed record PruneOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    bool CompactAfterPrune = false,
    bool Interactive = true) : BorgOperationOptions(Repository, ProgressSink, Interactive);