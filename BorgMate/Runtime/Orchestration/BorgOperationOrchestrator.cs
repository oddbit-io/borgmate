using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using BorgMate.Persistence.Journal;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using OneOf;

namespace BorgMate.Runtime.Orchestration;

/// <summary>Default runtime orchestrator for passphrase and transient retry behavior.</summary>
public sealed class BorgOperationOrchestrator(
    ILogger<BorgOperationOrchestrator> logger,
    IPassphraseProvider passphraseProvider,
    TransientRetryPolicy transientRetryPolicy,
    BorgJobQueue jobQueue,
    IJournalStore journalStore)
{
    private const int MaxPassphraseAttempts = 3;

    public async Task<OneOf<BorgExecutionResult, OperationError>> ExecuteAsync(
        BorgOperationRequest request,
        Func<BorgOperationContext, CancellationToken, Task<BorgResult>> operation,
        CancellationToken ct = default)
    {
        try
        {
            if (request.RequiresPassphrase)
            {
                var ensure = await passphraseProvider.EnsureAsync(request.Repository, ct);
                if (ensure.IsT1)
                    return HandlePassphraseFailure(request.Repository, ensure.AsT1);
            }

            for (var attempt = 0; attempt < MaxPassphraseAttempts; attempt++)
            {
                var context = new BorgOperationContext(request.Repository, request.ProgressSink);
                var result = await transientRetryPolicy.ExecuteAsync(
                    token => operation(context, token),
                    request.ProgressSink,
                    ct);

                if (result.IsT0)
                    return result.AsT0;

                var error = result.AsT1;
                if (error is not OperationError.BorgFailure failure ||
                    !BorgModernExitCodeMapper.IsWrongPassphrase(failure.Code) ||
                    !request.RequiresPassphrase)
                    return error;

                if (attempt >= MaxPassphraseAttempts - 1)
                    break;

                var reprompt = await passphraseProvider.RePromptAsync(request.Repository, ct);
                if (reprompt.IsT1)
                    return HandlePassphraseFailure(request.Repository, reprompt.AsT1);
            }

            return HandlePassphraseFailure(request.Repository, new OperationError.Cancelled());
        }
        catch (OperationCanceledException)
        {
            return new OperationError.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected exception while orchestrating borg operation for {Repository}",
                request.Repository.Name);
            return new OperationError.Unexpected(ex);
        }
    }

    private OperationError HandlePassphraseFailure(BorgRepository repo, OperationError error)
    {
        passphraseProvider.Clear(repo);
        passphraseProvider.MarkWrong(repo);

        void CompleteFailure()
        {
            jobQueue.CancelPendingByRepoPath(repo.Path);
            var entry = journalStore.Add(JournalEventKind.PassphraseFailed, [repo.Name], repo.Name);
            journalStore.Complete(entry, JournalResult.Failed,
                BorgErrorMessages.GetMessage(BorgModernExitCode.PassphraseWrong));
        }

        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            CompleteFailure();
        else
            Dispatcher.UIThread.Post(CompleteFailure);

        return error;
    }
}
