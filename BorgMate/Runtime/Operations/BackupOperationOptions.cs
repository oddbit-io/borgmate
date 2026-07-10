using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for creating a backup archive.</summary>
public sealed record BackupOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string? ArchiveName = null,
    bool Interactive = true) : BorgOperationOptions(Repository, ProgressSink, Interactive);