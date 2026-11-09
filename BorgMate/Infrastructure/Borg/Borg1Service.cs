using System;
using System.Collections.Generic;
using System.Globalization;
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
        var env = await BuildInitEnvironmentAsync(repo, ct);
        var args = new List<string> { "init", "--encryption", repo.EncryptionMode.ToBorgString(), "--", Location(repo) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CreateBackupAsync(
        BorgRepository repo, string archiveName,
        IEnumerable<string> sourcePaths, CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "create", "--progress" };
        args.AddRange(BackupLockWaitArgs);
        if (repo.RateLimit > 0)
            args.AddRange(["--remote-ratelimit", repo.RateLimit.ToString(CultureInfo.InvariantCulture)]);
        // --exclude-dataless (borg 1.4.5+) only has an effect on macOS but is accepted everywhere.
        if (repo.ExcludeCloudFiles && await SupportsFeatureAsync(BorgCliFeature.ExcludeDataless, ct))
            args.Add("--exclude-dataless");
        args.AddRange(BuildExcludeArgs(repo.ExcludedPaths));
        args.AddRange(["--", ArchiveLocation(repo, archiveName), .. sourcePaths.Select(P)]);
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ListArchivesAsync(
        BorgRepository repo, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "list", "--json", "--", Location(repo) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> AcceptRelocationAsync(
        BorgRepository repo, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct, acceptRelocation: true);
        var args = new List<string> { "list", "--short", "--last", "1", "--", Location(repo) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoArchiveAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "info", "--json", "--", ArchiveLocation(repo, archiveName) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> ListArchiveContentsAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "list", "--json-lines", "--", ArchiveLocation(repo, archiveName) };
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
        var args = new List<string> { "extract", "--progress" };
        args.AddRange(BuildExtractOptionArgs(extractOptions));
        args.AddRange(["--", ArchiveLocation(repo, archiveName), .. paths ?? []]);
        return await RunCommandAsync(BorgBinary, args, env, P(restorePath), ct, onStderrLine);
    }

    public override async Task<BorgResult> DeleteArchiveAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "delete", "--", ArchiveLocation(repo, archiveName) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> PruneAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        // --quick-stats (borg 1.4.5+) skips the slow "All archives" repository statistics.
        var stats = await SupportsFeatureAsync(BorgCliFeature.QuickStats, ct) ? "--quick-stats" : "--stats";
        var args = new List<string> { "prune", stats, "--list" };
        args.AddRange(BuildPruneRetentionArgs(repo.PruneOptions));
        args.AddRange(["--", Location(repo)]);
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CheckAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "check", "--progress", "--", Location(repo) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> CompactAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "compact", "--progress", "--", Location(repo) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ChangePassphraseAsync(
        BorgRepository repo, string newPassphrase,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, newPassphrase, ct);
        var args = new List<string> { "key", "change-passphrase", "--", Location(repo) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoRepoAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "info", "--json", "--", Location(repo) };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo, ct: ct);
        var args = new List<string> { "diff", "--", ArchiveLocation(repo, archive1), archive2 };
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }
}
