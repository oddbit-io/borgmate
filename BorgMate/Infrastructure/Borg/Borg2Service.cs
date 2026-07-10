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

public class Borg2Service(
    ILogger<Borg2Service> logger,
    AppSettings settings,
    SshAgentHelper sshAgent,
    WslHelper wsl,
    INetworkShareMounter shareMounter,
    PromptingPassphraseProvider? passphrase = null) : Borg2ServiceBase(logger, settings, sshAgent, wsl,
    shareMounter, passphrase ?? new PromptingPassphraseProvider(null))
{
    /// <summary>Detected Borg 2 CLI version used for version-specific command mapping.</summary>
    public BorgCliVersion? DetectedVersion { get; set; }

    public override async Task<BorgCliVersionValidationResult> ValidateCliVersionAsync(CancellationToken ct = default)
    {
        var result = await ParseCliVersionAsync(ct);
        if (!result.IsValid)
            return result;

        if (result.Version is not { Major: 2 })
            return BorgCliVersionValidationResult.Validation("Validation.BorgVersionMismatch", result.Version);

        if (!result.Version.IsSupportedBorg2)
            return BorgCliVersionValidationResult.Validation("Validation.Borg2TooOld", result.Version);

        DetectedVersion = result.Version;
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
        var encryptionArgs = Borg2EncryptionModeMapper.BuildRepoCreateArgs(repo.Borg2EncryptionMode, DetectedVersion);
        var args = $"repo-create {encryptionArgs}";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
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
        var args = $"create --progress{rateLimit} \"{archiveName}\" {paths}";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ListArchivesAsync(
        BorgRepository repo, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = "repo-list --json";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoArchiveAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = $"info --json \"{archiveName}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> ListArchiveContentsAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = $"list --json-lines \"{archiveName}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
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
        return await RunCommandAsync(BorgBinary, args, env, P(restorePath), ct, onStderrLine);
    }

    public override async Task<BorgResult> DeleteArchiveAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = $"delete \"{archiveName}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> PruneAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var retention = BuildPruneRetentionArgs(repo.PruneOptions);
        var args = $"prune --list{retention}";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CheckAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = "check --progress";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> CompactAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = "compact --progress";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ChangePassphraseAsync(
        BorgRepository repo, string newPassphrase,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, newPassphrase, ct);
        AddRepositoryEnvironment(repo, env);
        var args = "key change-passphrase";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoRepoAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = "repo-info --json";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        AddRepositoryEnvironment(repo, env);
        var args = $"diff \"{archive1}\" \"{archive2}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }
}
