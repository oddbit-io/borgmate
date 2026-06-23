using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for checking repository consistency.</summary>
public sealed record CheckOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink) : BorgOperationOptions(Repository, ProgressSink);