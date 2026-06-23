using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using BorgMate.Persistence.Journal;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;
using OneOf;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Runtime.Operations;

/// <summary>Default repository verification runner used by the repository editor.</summary>
public sealed class RepositoryVerificationOperationRunner(
    BorgServiceFactory borgServiceFactory,
    BorgJobQueue jobQueue,
    IJournalStore journalStore,
    IBorgOperationOrchestrator orchestrator,
    PromptingPassphraseProvider passphrasePrompt,
    WslHelper wsl) : IRepositoryVerificationOperationRunner
{
    public async Task<OneOf<VersionDetectionResult, OperationError>> DetectVersionAsync(
        VersionDetectionOperationOptions options, CancellationToken ct = default)
    {
        try
        {
            var service = borgServiceFactory.GetService(options.Repository.BorgVersion);
            var result = await service.GetVersionAsync(ct);
            return result.Success
                ? new VersionDetectionResult(result.StandardOutput.Trim(), BorgCliVersion.Parse(result.StandardOutput))
                : result.ToOperationError();
        }
        catch (OperationCanceledException)
        {
            return new OperationError.Cancelled();
        }
        catch (Exception ex)
        {
            return new OperationError.Unexpected(ex);
        }
    }

    public async Task<OneOf<RepositoryVerificationResult, OperationError>> VerifyNewRepositoryAsync(
        RepositoryVerificationOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var versionError = await ValidateConfiguredBorgAsync(service, ct);
        if (versionError is not null)
            return versionError;

        if (repo.IsEncrypted && string.IsNullOrWhiteSpace(passphrasePrompt.GetCachedPassphrase(repo)))
        {
            var ensured = await passphrasePrompt.EnsurePassphraseAsync(repo);
            if (!ensured)
                return new OperationError.Cancelled();
        }

        var journalEntry = journalStore.Add(JournalEventKind.Create, [repo.Name], repo.Name);
        var job = jobQueue.Enqueue(
            $"{Strings.Get("Job.Init")}: {repo.Name}",
            async (j, token, progress) =>
            {
                progress.Report(Strings.Get("Status.InitializingRepo"));
                return await service.InitAsync(repo, token);
            },
            repoPath: repo.Path,
            journalEntry: journalEntry);

        using var registration = ct.Register(() => job.Cts.Cancel());
        var result = await job.Completion.Task;

        if (result.Success)
        {
            journalStore.Complete(journalEntry, JournalResult.Completed);
            return new RepositoryVerificationResult(repo, true);
        }

        if (result.WasCancelled)
        {
            journalStore.Complete(journalEntry, JournalResult.Cancelled);
            return new OperationError.Cancelled();
        }

        journalStore.Complete(journalEntry, JournalResult.Failed, result.ErrorMessage);
        passphrasePrompt.Clear(repo);
        return result.ToOperationError();
    }

    public async Task<OneOf<RepositoryVerificationResult, OperationError>> VerifyExistingRepositoryAsync(
        RepositoryVerificationOperationOptions options, CancellationToken ct = default)
    {
        var repo = options.Repository;
        var service = borgServiceFactory.GetService(repo.BorgVersion);
        var versionError = await ValidateConfiguredBorgAsync(service, ct);
        if (versionError is not null)
            return versionError;

        var job = jobQueue.Enqueue(
            $"{Strings.Get("Job.VerifyRepo")}: {repo.Name}",
            async (j, token, progress) =>
            {
                progress.Report(Strings.Get("Status.VerifyingRepo"));
                var sink = new BorgJobProgressSink(j, options.ProgressSink);
                var result = await orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink),
                    (_, operationToken) => service.InfoRepoAsync(repo, operationToken),
                    token);
                return result.ToBorgResult();
            },
            BorgJobKind.Query,
            repoPath: repo.Path);

        using var registration = ct.Register(() => job.Cts.Cancel());
        var result = await job.Completion.Task;

        if (result.Success)
            return new RepositoryVerificationResult(repo, false);

        passphrasePrompt.Clear(repo);
        return result.WasCancelled
            ? new OperationError.Cancelled()
            : result.ToOperationError();
    }

    public async Task<OneOf<RemotePathCheckResult, OperationError>> CheckRemotePathAsync(
        RemotePathCheckOperationOptions options, CancellationToken ct = default)
    {
        try
        {
            var service = borgServiceFactory.GetService(options.Repository.BorgVersion);
            var result = await service.CheckRemotePathAsync(
                options.Host,
                options.RemotePath,
                options.SshKeyPath,
                ct);
            return result.Success
                ? new RemotePathCheckResult(options.RemotePath)
                : result.ToOperationError();
        }
        catch (OperationCanceledException)
        {
            return new OperationError.Cancelled();
        }
        catch (Exception ex)
        {
            return new OperationError.Unexpected(ex);
        }
    }

    public async Task<OneOf<SshKeyPreparationResult, OperationError>> PrepareSshKeyAsync(
        SshKeyPreparationOperationOptions options, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var service = borgServiceFactory.GetService(options.Repository.BorgVersion);
            if (WslHelper.IsRequired && service.UsesSshTransport(options.Repository) &&
                !string.IsNullOrWhiteSpace(options.SshKeyPath))
                await wsl.CopySshKeyAsync(options.SshKeyPath, ct);
            return new SshKeyPreparationResult(options.SshKeyPath, null);
        }
        catch (OperationCanceledException)
        {
            return new OperationError.Cancelled();
        }
        catch (Exception ex)
        {
            return new OperationError.Unexpected(ex);
        }
    }

    internal static async Task<OperationError?> ValidateConfiguredBorgAsync(IBorgService service, CancellationToken ct)
    {
        var validation = await service.ValidateCliVersionAsync(ct);
        if (validation.IsValid)
            return null;

        if (validation.FailureResult is { } failure)
            return failure.ToOperationError();

        return new OperationError.Validation(validation.ValidationMessageKey ?? "Validation.BorgVersionUnknown");
    }
}
