using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for comparing two archives.</summary>
public sealed record ArchiveDiffOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string PreviousArchiveName,
    string ArchiveName,
    bool ForceRefresh = false) : BorgOperationOptions(Repository, ProgressSink);