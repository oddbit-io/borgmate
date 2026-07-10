using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for compacting repository storage.</summary>
public sealed record CompactOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    bool Interactive = true) : BorgOperationOptions(Repository, ProgressSink, Interactive);