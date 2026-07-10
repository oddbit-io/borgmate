using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Persistence.Keychain;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using BorgMate.Runtime.Security;
using OneOf;

namespace BorgMate.Runtime.Operations;

/// <summary>Default repository command runner backed by the borg job queue.</summary>
public sealed class RepositoryOperationRunner(
    BorgServiceFactory borgServiceFactory,
    BorgJobQueue jobQueue,
    IJournalStore journalStore,
    BorgOperationOrchestrator orchestrator,
    DirectorySizeCalculator sizeCalculator,
    BorgCacheService cache,
    IRepositoryConfigStore repositoryStore,
    ISettingsStore settingsStore,
    ICredentialStore credentialStore,
    PromptingPassphraseProvider passphrasePrompt,
    INotificationService? notifier = null) : IRepositoryOperationRunner
{
    public async Task<OneOf<OperationOk, OperationError>> BackupAsync(
        BackupOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var archiveName = options.ArchiveName;
        if (string.IsNullOrWhiteSpace(archiveName))
        {
            var prefix = string.IsNullOrWhiteSpace(repo.ArchiveNamePrefix) ? repo.Name : repo.ArchiveNamePrefix;
            archiveName = $"{prefix}-{DateTime.Now:yyyy-MM-ddTHH-mm-ss}";
        }

        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunCommandAsync(
            repo,
            options.ProgressSink,
            JournalEventKind.Backup,
            [repo.Name],
            $"{Strings.Get("Job.Backup")}: {repo.Name}",
            async (job, token, sink) =>
            {
                job.StatusMessage = Strings.Get("Status.CalculatingSize");
                var (totalSize, totalFiles) = sizeCalculator.Calculate(repo.SourceDirectories, token,
                    (dirs, files, size) =>
                    {
                        var msg = string.Format(
                            Strings.Get("Status.CalculatingSizeProgress"),
                            dirs.ToString("N0", Strings.Culture),
                            files.ToString("N0", Strings.Culture),
                            Strings.FormatBytes(size));
                        Dispatcher.UIThread.Post(() => job.StatusMessage = msg);
                    });

                if (token.IsCancellationRequested)
                    return new OperationError.Cancelled();

                job.TotalSize = totalSize;
                job.TotalFileCount = totalFiles;

                return await orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink, Interactive: options.Interactive),
                    (_, operationToken) => service.CreateBackupAsync(
                        repo,
                        archiveName,
                        repo.SourceDirectories,
                        operationToken,
                        line => service.UpdateProgress(job, line)),
                    token);
            },
            true,
            ct);

        if (result.IsT1)
            return result.AsT1;

        repo.LastBackupAt = DateTime.Now;
        await repositoryStore.UpdateAsync(repo, ct);
        settingsStore.RequestSave();
        return OperationOk.Instance;
    }

    public async Task<OneOf<PruneResult, OperationError>> PruneAsync(
        PruneOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunCommandAsync(
            repo,
            options.ProgressSink,
            JournalEventKind.Prune,
            [repo.Name],
            $"{Strings.Get("Job.Prune")}: {repo.Name}",
            (job, token, sink) =>
            {
                job.StatusMessage = string.Format(Strings.Get("Status.Pruning"), repo.Name);
                return orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink, Interactive: options.Interactive),
                    (_, operationToken) => service.PruneAsync(repo, operationToken),
                    token);
            },
            true,
            ct);

        return result.IsT1
            ? result.AsT1
            : new PruneResult(DateTimeOffset.Now, options.CompactAfterPrune || repo.PruneOptions.CompactAfterPrune);
    }

    public async Task<OneOf<OperationOk, OperationError>> CheckAsync(
        CheckOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunCommandAsync(
            repo,
            options.ProgressSink,
            JournalEventKind.Check,
            [repo.Name],
            $"{Strings.Get("Job.Check")}: {repo.Name}",
            (job, token, sink) =>
            {
                job.ProgressLabel = Strings.Get("Status.CheckingProgress");
                job.StatusMessage = job.ProgressLabel;
                return orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink),
                    (_, operationToken) => service.CheckAsync(
                        repo,
                        operationToken,
                        line => service.UpdateProgress(job, line)),
                    token);
            },
            false,
            ct);

        return result.IsT1 ? result.AsT1 : OperationOk.Instance;
    }

    public async Task<OneOf<OperationOk, OperationError>> CompactAsync(
        CompactOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunCommandAsync(
            repo,
            options.ProgressSink,
            JournalEventKind.Compact,
            [repo.Name],
            $"{Strings.Get("Job.Compact")}: {repo.Name}",
            (job, token, sink) =>
            {
                job.ProgressLabel = Strings.Get("Status.CompactingProgress");
                job.StatusMessage = job.ProgressLabel;
                return orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink, Interactive: options.Interactive),
                    (_, operationToken) => service.CompactAsync(
                        repo,
                        operationToken,
                        line => service.UpdateProgress(job, line)),
                    token);
            },
            true,
            ct);

        return result.IsT1 ? result.AsT1 : OperationOk.Instance;
    }

    public async Task<OneOf<OperationOk, OperationError>> RestoreAsync(
        RestoreOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunCommandAsync(
            repo,
            options.ProgressSink,
            JournalEventKind.Restore,
            [options.ArchiveName],
            $"{Strings.Get("Job.Restore")}: {options.ArchiveName} -> {options.DestinationPath}",
            (job, token, sink) =>
            {
                job.TotalSize = options.TotalSize;
                job.StatusMessage = string.Format(Strings.Get("Status.Restoring"), options.ArchiveName);
                return orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink),
                    (_, operationToken) => service.ExtractAsync(
                        repo,
                        options.ArchiveName,
                        options.DestinationPath,
                        options.ExtractOptions,
                        options.Paths,
                        operationToken,
                        line => service.UpdateProgress(job, line)),
                    token);
            },
            false,
            ct);

        return result.IsT1 ? result.AsT1 : OperationOk.Instance;
    }

    public async Task<OneOf<OperationOk, OperationError>> DeleteArchiveAsync(
        DeleteArchiveOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunCommandAsync(
            repo,
            options.ProgressSink,
            JournalEventKind.Delete,
            [options.ArchiveName],
            $"{Strings.Get("Job.DeleteArchive")}: {options.ArchiveName}",
            (job, token, sink) => orchestrator.ExecuteAsync(
                new BorgOperationRequest(repo, sink),
                (_, operationToken) => service.DeleteArchiveAsync(repo, options.ArchiveName, operationToken),
                token),
            true,
            ct);

        return result.IsT1 ? result.AsT1 : OperationOk.Instance;
    }

    public async Task<OneOf<OperationOk, OperationError>> ChangePassphraseAsync(
        ChangePassphraseOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var result = await RunCommandAsync(
            repo,
            options.ProgressSink,
            JournalEventKind.ChangePassphrase,
            [repo.Name],
            $"{Strings.Get("Job.ChangePassphrase")}: {repo.Name}",
            (job, token, sink) =>
            {
                job.StatusMessage = string.Format(Strings.Get("Status.ChangingPassphrase"), repo.Name);
                return orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink),
                    (_, operationToken) => service.ChangePassphraseAsync(repo, options.NewPassphrase, operationToken),
                    token);
            },
            false,
            ct);

        if (result.IsT1)
            return result.AsT1;

        passphrasePrompt.Set(repo, options.NewPassphrase);
        if (options.SaveToCredentialStore)
            await credentialStore.TrySetPassphraseAsync(notifier, repo.Path, options.NewPassphrase);
        else
            await credentialStore.DeletePassphraseAsync(repo.Path);

        return OperationOk.Instance;
    }

    private async Task<OneOf<BorgExecutionResult, OperationError>> RunCommandAsync(
        BorgRepository repo,
        IOperationProgressSink progressSink,
        JournalEventKind kind,
        object[] titleArgs,
        string jobName,
        Func<BorgJob, CancellationToken, IOperationProgressSink, Task<OneOf<BorgExecutionResult, OperationError>>>
            execute,
        bool invalidateArchivesOnSuccess,
        CancellationToken ct)
    {
        var journalEntry = journalStore.Add(kind, titleArgs, repo.Name);
        var job = jobQueue.Enqueue(
            jobName,
            async (j, token, _) =>
            {
                var sink = new BorgJobProgressSink(j, progressSink);
                var execution = await execute(j, token, sink);
                return execution.ToBorgResult();
            },
            repoPath: repo.Path,
            journalEntry: journalEntry);

        using var registration = ct.Register(() => job.Cts.Cancel());
        var result = await job.Completion.Task;
        passphrasePrompt.Clear(repo);

        if (result.Success)
        {
            journalStore.Complete(journalEntry, JournalResult.Completed);
            if (invalidateArchivesOnSuccess)
                cache.InvalidateRepo(repo.Path);
            return new BorgExecutionResult(result);
        }

        if (result.WasCancelled)
        {
            journalStore.Complete(journalEntry, JournalResult.Cancelled);
            return new OperationError.Cancelled();
        }

        journalStore.Complete(journalEntry, JournalResult.Failed, result.ErrorMessage);
        return result.ToOperationError();
    }
}
