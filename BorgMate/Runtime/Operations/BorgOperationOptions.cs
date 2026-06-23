using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Base options shared by repository-scoped borg runtime operations.</summary>
public abstract record BorgOperationOptions(BorgRepository Repository, IOperationProgressSink ProgressSink);