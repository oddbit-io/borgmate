using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Borg;
using BorgMate.Runtime.Operations;
using OneOf;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Coordinates passphrase retry and transient retry around a raw borg operation.</summary>
public interface IBorgOperationOrchestrator
{
    /// <summary>Executes <paramref name="operation" /> and maps expected borg outcomes to runtime results.</summary>
    Task<OneOf<BorgExecutionResult, OperationError>> ExecuteAsync(
        BorgOperationRequest request,
        Func<BorgOperationContext, CancellationToken, Task<BorgResult>> operation,
        CancellationToken ct = default);
}