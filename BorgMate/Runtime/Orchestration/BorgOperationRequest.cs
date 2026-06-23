using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Runtime request metadata shared by orchestrated borg operations.</summary>
public sealed record BorgOperationRequest(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    bool RequiresPassphrase = true);