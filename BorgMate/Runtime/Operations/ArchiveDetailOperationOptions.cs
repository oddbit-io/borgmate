using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for loading detail for a single archive.</summary>
public sealed record ArchiveDetailOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string ArchiveName) : BorgOperationOptions(Repository, ProgressSink);