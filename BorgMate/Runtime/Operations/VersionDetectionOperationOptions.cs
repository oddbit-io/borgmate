using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for detecting the configured borg binary version.</summary>
public sealed record VersionDetectionOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink) : BorgOperationOptions(Repository, ProgressSink);