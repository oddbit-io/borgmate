using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for deleting an archive from a repository.</summary>
public sealed record DeleteArchiveOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string ArchiveName) : BorgOperationOptions(Repository, ProgressSink);