using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Context passed to a raw borg operation while it is being orchestrated.</summary>
public sealed record BorgOperationContext(BorgRepository Repository, IOperationProgressSink ProgressSink);