using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Infrastructure.Borg;

public class Borg2Service : Borg2ServiceBase
{
    public Borg2Service(
        ILogger<Borg2Service> logger,
        AppSettings settings,
        SshRepositoryTransport sshTransport,
        WslHelper wsl,
        INetworkShareMounter shareMounter,
        PromptingPassphraseProvider? passphrase = null)
        : base(logger, settings, sshTransport, wsl, shareMounter,
            passphrase ?? new PromptingPassphraseProvider(null))
    {
    }

    public override async Task<BorgCliVersionValidationResult> ValidateCliVersionAsync(CancellationToken ct = default)
    {
        var result = await ParseCliVersionAsync(ct);
        if (!result.IsValid)
            return result;

        if (result.Version is not { Major: 2 })
            return BorgCliVersionValidationResult.Validation("Validation.BorgVersionMismatch", result.Version);

        if (!result.Version.IsSupportedBorg2)
            return BorgCliVersionValidationResult.Validation("Validation.Borg2Unsupported", result.Version);

        return result;
    }

    public override void UpdateProgress(BorgJob job, string line)
    {
        Borg2ProgressParser.Update(job, line);
    }

    public override async Task<BorgResult> InitAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var encryptionArgs = Borg2EncryptionModeMapper.BuildRepoCreateArgs(repo.Borg2EncryptionMode);
        var args = $"repo-create {encryptionArgs}";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CreateBackupAsync(
        BorgRepository repo, string archiveName,
        IEnumerable<string> sourcePaths, CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var paths = string.Join(" ", sourcePaths.Select(p => $"\"{P(p)}\""));
        var rateLimit = repo.RateLimit > 0 ? $" --upload-ratelimit {repo.RateLimit}" : "";
        var excludes = BuildExcludeArgs(repo.ExcludedPaths);
        var args = $"create --progress{rateLimit}{excludes} \"{archiveName}\" {paths}";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ListArchivesAsync(
        BorgRepository repo, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = "repo-list --json";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoArchiveAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = $"info --json \"{archiveName}\"";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> ListArchiveContentsAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = $"list --json-lines \"{archiveName}\"";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> ExtractAsync(
        BorgRepository repo, string archiveName,
        string restorePath, BorgExtractOptions extractOptions,
        IReadOnlyList<string>? paths = null,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var optionArgs = BuildExtractOptionArgs(extractOptions);
        var pathArgs = paths is { Count: > 0 }
            ? " " + string.Join(" ", paths.Select(p => $"\"{p}\""))
            : "";
        var args = $"extract --progress{optionArgs} \"{archiveName}\"{pathArgs}";
        return await RunSupportedCommandAsync(BorgBinary, args, env, P(restorePath), ct, onStderrLine);
    }

    public override async Task<BorgResult> DeleteArchiveAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = $"delete \"{archiveName}\"";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> PruneAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var retention = BuildPruneRetentionArgs(repo.PruneOptions);
        var args = $"prune --list{retention}";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CheckAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = "check --progress";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> CompactAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = "compact --progress";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ChangePassphraseAsync(
        BorgRepository repo, string newPassphrase,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, newPassphrase, ct);
        AddRepositoryEnvironment(repo, env);
        var args = "key change-passphrase";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoRepoAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = "repo-info --json";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = $"diff \"{archive1}\" \"{archive2}\"";
        return await RunSupportedCommandAsync(BorgBinary, args, env, ct: ct);
    }

    private async Task<BorgResult> RunSupportedCommandAsync(
        string fileName,
        string arguments,
        BorgEnv? env = null,
        string? workingDirectory = null,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var validation = await ValidateCliVersionAsync(ct);
        if (validation.FailureResult is { } failure)
            return failure;

        if (!validation.IsValid)
            return new BorgResult(-1, "", Strings.Get(
                validation.ValidationMessageKey ?? "Validation.BorgVersionUnknown"));

        return await RunCommandAsync(fileName, arguments, env, workingDirectory, ct, onStderrLine);
    }
}
