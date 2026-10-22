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

public class Borg1Service : BorgServiceBase
{
    public Borg1Service(
        ILogger<Borg1Service> logger,
        AppSettings settings,
        SshRepositoryTransport sshTransport,
        WslHelper wsl,
        INetworkShareMounter shareMounter,
        PromptingPassphraseProvider? passphrase = null)
        : base(logger, settings, BorgVersion.Borg1, sshTransport, wsl, shareMounter,
            passphrase ?? new PromptingPassphraseProvider(null))
    {
    }

    public override async Task<BorgCliVersionValidationResult> ValidateCliVersionAsync(CancellationToken ct = default)
    {
        var result = await ParseCliVersionAsync(ct);
        if (!result.IsValid)
            return result;

        return result.Version is { Major: 1 }
            ? result
            : BorgCliVersionValidationResult.Validation("Validation.BorgVersionMismatch", result.Version);
    }

    public override void UpdateProgress(BorgJob job, string line)
    {
        Borg1ProgressParser.Update(job, line);
    }

    public override async Task<BorgResult> InitAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"init --encryption {repo.EncryptionMode.ToBorgString()} \"{Location(repo)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CreateBackupAsync(
        BorgRepository repo, string archiveName,
        IEnumerable<string> sourcePaths, CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var paths = string.Join(" ", sourcePaths.Select(p => $"\"{P(p)}\""));
        var rateLimit = repo.RateLimit > 0 ? $" --remote-ratelimit {repo.RateLimit}" : "";
        // --exclude-dataless (borg 1.4.5+) only has an effect on macOS but is accepted everywhere.
        var excludeDataless =
            repo.ExcludeCloudFiles && await SupportsFeatureAsync(BorgCliFeature.ExcludeDataless, ct)
            ? " --exclude-dataless"
            : "";
        var excludes = BuildExcludeArgs(repo.ExcludedPaths);
        var args = $"create --progress{BackupLockWaitArgs}{rateLimit}{excludeDataless}{excludes} \"{Location(repo)}::{archiveName}\" {paths}";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ListArchivesAsync(
        BorgRepository repo, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"list --json \"{Location(repo)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoArchiveAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"info --json \"{Location(repo)}::{archiveName}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> ListArchiveContentsAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"list --json-lines \"{Location(repo)}::{archiveName}\"";
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
        var optionArgs = BuildExtractOptionArgs(extractOptions);
        var pathArgs = paths is { Count: > 0 }
            ? " " + string.Join(" ", paths.Select(p => $"\"{p}\""))
            : "";
        var args = $"extract --progress{optionArgs} \"{Location(repo)}::{archiveName}\"{pathArgs}";
        return await RunCommandAsync(BorgBinary, args, env, P(restorePath), ct, onStderrLine);
    }

    public override async Task<BorgResult> DeleteArchiveAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"delete \"{Location(repo)}::{archiveName}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> PruneAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var retention = BuildPruneRetentionArgs(repo.PruneOptions);
        // --quick-stats (borg 1.4.5+) skips the slow "All archives" repository statistics.
        var stats = await SupportsFeatureAsync(BorgCliFeature.QuickStats, ct) ? "--quick-stats" : "--stats";
        var args = $"prune {stats} --list{retention} \"{Location(repo)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CheckAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"check --progress \"{Location(repo)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> CompactAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"compact --progress \"{Location(repo)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ChangePassphraseAsync(
        BorgRepository repo, string newPassphrase,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, newPassphrase, ct);
        var args = $"key change-passphrase \"{Location(repo)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoRepoAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"info --json \"{Location(repo)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = $"diff \"{Location(repo)}::{archive1}\" \"{archive2}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }
}
