using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Borg;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Progress;
using Microsoft.Extensions.Logging;
using OneOf;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Default retry policy for transient borg and SSH failures.</summary>
public sealed class TransientRetryPolicy(ILogger<TransientRetryPolicy> logger)
{
    private static readonly int[] RetryDelays = [5, 10, 30, 60, 300];

    public async Task<OneOf<BorgExecutionResult, OperationError>> ExecuteAsync(
        Func<CancellationToken, Task<BorgResult>> operation,
        IOperationProgressSink progressSink,
        CancellationToken ct = default)
    {
        for (var attempt = 0;; attempt++)
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await operation(ct);

                if (result.Completed)
                    return new BorgExecutionResult(result);

                if (result.WasCancelled)
                    return new OperationError.Cancelled();

                if (!BorgModernExitCodeMapper.IsRetryable(result.ErrorCode) || attempt >= RetryDelays.Length)
                    return result.ToOperationError();

                var delaySec = RetryDelays[attempt];
                logger.LogWarning("Transient borg error ({ErrorCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    result.ErrorCode, delaySec, attempt + 1, RetryDelays.Length);

                for (var remaining = delaySec; remaining > 0; remaining--)
                {
                    progressSink.Report(new OperationProgressUpdate
                    {
                        StatusKey = "Status.RetryingIn",
                        Retry = new RetryCountdown(attempt + 1, RetryDelays.Length, TimeSpan.FromSeconds(remaining))
                    });
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }

                progressSink.Report(new OperationProgressUpdate
                {
                    StatusKey = "Status.RetryAttempt",
                    Retry = new RetryCountdown(attempt + 1, RetryDelays.Length, TimeSpan.Zero),
                    Percent = null
                });
            }
            catch (OperationCanceledException)
            {
                return new OperationError.Cancelled();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected exception while executing retryable borg operation");
                return new OperationError.Unexpected(ex);
            }
    }
}
