using System.Reflection;
using BorgMate.Infrastructure.Borg;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;

namespace BorgMate.Tests.Runtime;

public class RuntimeContractTests
{
    [Fact]
    public void OperationRunnerMethods_AcceptSingleOptionsTypeAndOptionalCancellationToken()
    {
        var runnerTypes = new[]
        {
            typeof(IRepositoryOperationRunner),
            typeof(IArchiveOperationRunner),
            typeof(IRepositoryVerificationOperationRunner)
        };

        var violations = runnerTypes
            .SelectMany(t => t.GetMethods().Select(m => ValidateOperationRunnerMethod(t, m)))
            .Where(v => v is not null)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void OperationOptions_AllDeriveFromBorgOperationOptions()
    {
        var optionTypes = typeof(BorgOperationOptions).Assembly.GetTypes()
            .Where(t => t.Namespace == "BorgMate.Runtime.Operations" &&
                        t is { IsAbstract: false, IsClass: true } &&
                        t.Name.EndsWith("OperationOptions", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(optionTypes);
        Assert.All(optionTypes, t => Assert.True(
            typeof(BorgOperationOptions).IsAssignableFrom(t),
            $"{t.FullName} must derive from {nameof(BorgOperationOptions)}"));
    }

    [Fact]
    public void OperationProgressUpdate_CarriesPercentByteCountersAndRetryCountdown()
    {
        var counters = new ByteCounters(OriginalBytes: 10, CompressedBytes: 6, DeduplicatedBytes: 4, FileCount: 2);
        var retry = new RetryCountdown(Attempt: 1, MaxAttempts: 5, RemainingDelay: TimeSpan.FromSeconds(3));

        var update = new OperationProgressUpdate
        {
            Percent = 42.5,
            Counters = counters,
            Retry = retry,
            StatusKey = "Status.RetryingIn"
        };

        Assert.Equal(42.5, update.Percent);
        Assert.Equal(counters, update.Counters);
        Assert.Equal(retry, update.Retry);
        Assert.Equal("Status.RetryingIn", update.StatusKey);
    }

    [Fact]
    public void NullOperationProgressSink_AcceptsAllProgressShapes()
    {
        var update = new OperationProgressUpdate
        {
            Percent = 99,
            Counters = new ByteCounters(OriginalBytes: 100, CompressedBytes: 50, DeduplicatedBytes: 25, FileCount: 10),
            Retry = new RetryCountdown(Attempt: 2, MaxAttempts: 5, RemainingDelay: TimeSpan.FromSeconds(1)),
            StatusKey = "Status.RetryAttempt"
        };

        NullOperationProgressSink.Instance.Report(update);
    }

    [Fact]
    public async Task TransientRetryPolicy_ReportsRetryCountdownThroughProgressSink()
    {
        using var cts = new CancellationTokenSource();
        var updates = new List<OperationProgressUpdate>();
        var sink = new RecordingProgressSink(update =>
        {
            updates.Add(update);
            if (update.Retry is not null)
                cts.Cancel();
        });

        var policy = new TransientRetryPolicy(Substitute.For<ILogger<TransientRetryPolicy>>());
        var attempts = 0;

        var result = await policy.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(new BorgResult(
                    (int)BorgModernExitCode.LockError,
                    string.Empty,
                    "Failed to create/acquire the lock: exclusive lock exists"));
            },
            sink,
            cts.Token);

        Assert.True(result.IsT1);
        Assert.IsType<OperationError.Cancelled>(result.AsT1);
        Assert.Equal(1, attempts);
        var retryUpdate = Assert.Single(updates, u => u.Retry is not null);
        Assert.Equal("Status.RetryingIn", retryUpdate.StatusKey);
        Assert.Equal(1, retryUpdate.Retry!.Attempt);
        Assert.Equal(5, retryUpdate.Retry.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), retryUpdate.Retry.RemainingDelay);
    }

    private static string? ValidateOperationRunnerMethod(Type runnerType, MethodInfo method)
    {
        // Synchronous void helpers (e.g. cache invalidation) are not borg operations;
        // the options contract only governs Task-returning operation methods.
        if (method.ReturnType == typeof(void))
            return null;

        var parameters = method.GetParameters();
        if (parameters.Length is < 1 or > 2)
            return $"{runnerType.Name}.{method.Name} must accept options plus optional CancellationToken.";

        var optionsType = parameters[0].ParameterType;
        if (!optionsType.Name.EndsWith("Options", StringComparison.Ordinal))
            return $"{runnerType.Name}.{method.Name} first parameter must be an options type.";

        if (!typeof(BorgOperationOptions).IsAssignableFrom(optionsType))
            return $"{runnerType.Name}.{method.Name} options must derive from {nameof(BorgOperationOptions)}.";

        if (parameters.Length == 2 && parameters[1].ParameterType != typeof(CancellationToken))
            return $"{runnerType.Name}.{method.Name} second parameter must be CancellationToken.";

        if (!method.ReturnType.IsGenericType ||
            method.ReturnType.GetGenericTypeDefinition() != typeof(Task<>))
        {
            return $"{runnerType.Name}.{method.Name} must return Task<OneOf<*, OperationError>>.";
        }

        var taskResultType = method.ReturnType.GetGenericArguments()[0];
        if (!taskResultType.IsGenericType ||
            taskResultType.GetGenericTypeDefinition() != typeof(OneOf<,>) ||
            taskResultType.GetGenericArguments()[1] != typeof(OperationError))
        {
            return $"{runnerType.Name}.{method.Name} must return Task<OneOf<*, OperationError>>.";
        }

        return null;
    }

    private sealed class RecordingProgressSink(Action<OperationProgressUpdate> onReport) : IOperationProgressSink
    {
        public void Report(OperationProgressUpdate update) => onReport(update);
    }
}
