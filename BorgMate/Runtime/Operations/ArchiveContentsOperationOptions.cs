using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for listing archive contents.</summary>
public sealed record ArchiveContentsOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string ArchiveName,
    bool ForceRefresh = false) : BorgOperationOptions(Repository, ProgressSink);