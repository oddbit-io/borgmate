using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for listing repository archives.</summary>
public sealed record ArchiveListOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    bool ForceRefresh = false) : BorgOperationOptions(Repository, ProgressSink);