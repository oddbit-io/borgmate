using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Runtime request metadata shared by orchestrated borg operations.</summary>
/// <param name="PersistRelocations">False for editor candidates whose configuration has not been committed.</param>
public sealed record BorgOperationRequest(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    bool RequiresPassphrase = true,
    bool Interactive = true,
    bool PersistRelocations = true);
