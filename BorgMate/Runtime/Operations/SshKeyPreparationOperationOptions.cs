using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for preparing an SSH key for borg access.</summary>
public sealed record SshKeyPreparationOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string SshKeyPath) : BorgOperationOptions(Repository, ProgressSink);