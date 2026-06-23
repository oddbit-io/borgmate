using System.Collections.Generic;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for restoring an archive or selected archive paths.</summary>
public sealed record RestoreOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string ArchiveName,
    string DestinationPath,
    BorgExtractOptions ExtractOptions,
    IReadOnlyList<string>? Paths = null,
    long TotalSize = 0) : BorgOperationOptions(Repository, ProgressSink);
