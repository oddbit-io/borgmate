using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Borg;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Progress;
using OneOf;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Retries transient borg failures with backoff while reporting neutral progress.</summary>
public interface ITransientRetryPolicy
{
    /// <summary>Executes <paramref name="operation" /> and retries retryable borg failures.</summary>
    Task<OneOf<BorgExecutionResult, OperationError>> ExecuteAsync(
        Func<CancellationToken, Task<BorgResult>> operation,
        IOperationProgressSink progressSink,
        CancellationToken ct = default);
}