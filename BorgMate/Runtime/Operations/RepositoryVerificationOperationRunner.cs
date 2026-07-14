using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Journal;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;
using OneOf;

namespace BorgMate.Runtime.Operations;

/// <summary>Default repository verification runner used by the repository editor.</summary>
public class RepositoryVerificationOperationRunner : IRepositoryVerificationOperationRunner
{
    private readonly BorgServiceFactory borgServiceFactory;
    private readonly BorgJobQueue jobQueue;
    private readonly IJournalStore journalStore;
    private readonly BorgOperationOrchestrator orchestrator;
    private readonly PromptingPassphraseProvider passphrasePrompt;
    private readonly SshRepositoryTransport sshTransport;

    public RepositoryVerificationOperationRunner(
        BorgServiceFactory borgServiceFactory,
        BorgJobQueue jobQueue,
        IJournalStore journalStore,
        BorgOperationOrchestrator orchestrator,
        PromptingPassphraseProvider passphrasePrompt,
        SshRepositoryTransport sshTransport)
    {
        this.borgServiceFactory = borgServiceFactory;
        this.jobQueue = jobQueue;
        this.journalStore = journalStore;
        this.orchestrator = orchestrator;
        this.passphrasePrompt = passphrasePrompt;
        this.sshTransport = sshTransport;
    }

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

    public async Task<OneOf<OperationOk, OperationError>> VerifyNewRepositoryAsync(
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
            return OperationOk.Instance;
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

    public async Task<OneOf<OperationOk, OperationError>> VerifyExistingRepositoryAsync(
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

                // Probe without prompting first: unencrypted repos must open without a passphrase dialog.
                var hadPassphrase = !string.IsNullOrWhiteSpace(passphrasePrompt.GetCachedPassphrase(repo));
                var result = await orchestrator.ExecuteAsync(
                    new BorgOperationRequest(repo, sink, RequiresPassphrase: false),
                    (_, operationToken) => service.InfoRepoAsync(repo, operationToken),
                    token);

                if (result.IsT1 && result.AsT1 is OperationError.BorgFailure failure &&
                    BorgModernExitCodeMapper.IsWrongPassphrase(failure.Code))
                {
                    hadPassphrase = true;
                    result = await orchestrator.ExecuteAsync(
                        new BorgOperationRequest(repo, sink),
                        (_, operationToken) => service.InfoRepoAsync(repo, operationToken),
                        token);
                }

                if (result.IsT0)
                    ApplyDetectedEncryption(repo, result.AsT0.Result.StandardOutput, hadPassphrase);
                return result.ToBorgResult();
            },
            BorgJobKind.Query,
            repoPath: repo.Path);

        using var registration = ct.Register(() => job.Cts.Cancel());
        var result = await job.Completion.Task;

        if (result.Success)
            return OperationOk.Instance;

        passphrasePrompt.Clear(repo);
        return result.WasCancelled
            ? new OperationError.Cancelled()
            : result.ToOperationError();
    }

    public async Task<OneOf<OperationOk, OperationError>> CheckRemotePathAsync(
        RemotePathCheckOperationOptions options, CancellationToken ct = default)
    {
        try
        {
            if (options.RemotePath.StartsWith("-", StringComparison.Ordinal))
                return new OperationError.Unexpected(new ArgumentException("Invalid remote Borg path."));

            var repository = options.Repository with
            {
                Path = $"{options.Host}:.",
                LocationKind = BorgRepositoryLocationKind.SshFields,
                IsLocal = false,
                SshKeyPath = options.SshKeyPath ?? string.Empty,
                SshPort = options.SshPort
            };
            var target = new SshRepositoryTarget
            {
                Host = options.Host,
                RepositoryPath = ".",
                Port = options.SshPort
            };
            var command = $"command -v {SshRepositoryTransport.QuoteRemoteArgument(options.RemotePath)}";
            var result = await sshTransport.RunAsync(repository, target, command, ct);
            return result.Success ? OperationOk.Instance : result.ToOperationError();
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

    public async Task<OneOf<OperationOk, OperationError>> PrepareSshKeyAsync(
        SshKeyPreparationOperationOptions options, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await sshTransport.PrepareKeyAsync(options.Repository, options.SshKeyPath, ct);
            return OperationOk.Instance;
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

    /// <summary>
    ///     Writes the repository's real encryption mode (from "borg info" JSON) back onto the model,
    ///     so IsEncrypted reflects the on-disk repo instead of the editor default.
    /// </summary>
    private static void ApplyDetectedEncryption(BorgRepository repo, string infoJson, bool usedPassphrase)
    {
        var mode = ArchiveJsonParser.ParseEncryptionMode(infoJson);

        if (repo.BorgVersion == BorgVersion.Borg2)
        {
            if (EnumExtensions.Borg2EncryptionModeFromBorgString(mode) is { } borg2Mode)
                repo.Borg2EncryptionMode = borg2Mode;
            else if (!usedPassphrase)
                // Info succeeded with no passphrase at all: the repo is provably unencrypted.
                repo.Borg2EncryptionMode = Borg2EncryptionMode.None;
        }
        else
        {
            if (EnumExtensions.BorgEncryptionModeFromBorgString(mode) is { } borg1Mode)
                repo.EncryptionMode = borg1Mode;
            else if (!usedPassphrase)
                repo.EncryptionMode = BorgEncryptionMode.None;
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
