using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Localization;
using BorgMate.Models;
using BorgMate.Services.Journal;
using BorgMate.Services.Queue;
using BorgMate.Services.UI;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Borg;

/// <summary>
/// Runs borg operations with automatic passphrase re-prompt and transient error retry.
/// </summary>
public class BorgOperationRunner(
    ILogger<BorgOperationRunner> logger,
    JobQueueService jobQueue,
    IJournalService journalService,
    PassphrasePrompt passphrasePrompt)
{
    private const int MaxAttempts = 3;
    private static readonly int[] RetryDelays = [5, 10, 30, 60, 300];
    private static readonly BorgResult CancelledResult = new(-1, "", "", WasCancelled: true);

    /// <summary>
    /// Executes a borg operation with up to MaxAttempts passphrase prompts.
    /// On exhausted attempts or user cancel, clears passphrase, cancels pending repo jobs, and logs to journal.
    /// </summary>
    public async Task<BorgResult> RunWithPassphraseRetry(
        BorgRepository repo,
        Func<Task<BorgResult>> operation)
    {
        if (!await passphrasePrompt.EnsurePassphraseAsync(repo))
        {
            OnPassphraseFailed(repo);
            return CancelledResult;
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var result = await operation();

            if (result.Success)
                return result;

            if (!BorgErrorClassifier.IsWrongPassphrase(result.ErrorType))
                return result;

            // Last attempt — don't re-prompt, just fail
            if (attempt >= MaxAttempts - 1)
                break;

            if (!await passphrasePrompt.RePromptAsync(repo))
            {
                OnPassphraseFailed(repo);
                return CancelledResult;
            }
        }

        OnPassphraseFailed(repo);
        return CancelledResult;
    }

    /// <summary>
    /// Wraps a borg operation with automatic retry on transient errors
    /// (SSH connection issues, stale repository locks after SSH drops).
    /// Uses exponential backoff: 5s, 10s, 30s, 1min, 5min, then fails.
    /// </summary>
    public async Task<BorgResult> RunWithTransientRetry(
        BorgJob job,
        Func<Task<BorgResult>> operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            var result = await operation();

            if (result.Success || result.WasCancelled)
                return result;

            if (!BorgErrorClassifier.IsRetryable(result.ErrorType)
                || attempt >= RetryDelays.Length)
                return result;

            logger.LogWarning("Transient error ({ErrorType}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                result.ErrorType, RetryDelays[attempt], attempt + 1, RetryDelays.Length);

            var delaySec = RetryDelays[attempt];
            for (var s = delaySec; s > 0; s--)
            {
                if (job.Cts.IsCancellationRequested)
                    return new BorgResult(-1, "", "", WasCancelled: true);
                Dispatcher.UIThread.Post(() =>
                    job.StatusMessage = string.Format(Strings.Get("Status.RetryingIn"), s));
                await Task.Delay(1000);
            }

            var attemptNumber = attempt + 1;
            Dispatcher.UIThread.Post(() =>
            {
                job.StatusMessage = string.Format(
                    Strings.Get("Status.RetryAttempt"), attemptNumber, RetryDelays.Length);
                job.Progress = null;
            });
        }
    }

    private void OnPassphraseFailed(BorgRepository repo)
    {
        repo.Passphrase = string.Empty;
        repo.WrongPassphrase = true;

        // Marshal to UI thread — CancelPendingByRepoPath iterates Jobs (ObservableCollection)
        // and JournalService.Add modifies Entries, both of which must happen on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            jobQueue.CancelPendingByRepoPath(repo.Path);
            var entry = journalService.Add(
                JournalEventKind.PassphraseFailed, [repo.Name], repo.Name);
            journalService.Complete(entry, JournalResult.Failed, Strings.Get("Error.WrongPassphrase"));
        });
    }
}
