using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for verifying a repository candidate before saving it.</summary>
public sealed record RepositoryVerificationOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    bool InitializeWhenNew) : BorgOperationOptions(Repository, ProgressSink);