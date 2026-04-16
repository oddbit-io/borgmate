using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Models;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Borg;

public class Borg2Service(ILogger<Borg2Service> logger, AppSettings settings, SshAgentHelper sshAgent, WslHelper wsl) : BorgServiceBase(logger, settings, sshAgent, wsl)
{
    public override async Task<BorgResult> InitAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"rcreate --encryption {repo.EncryptionMode.ToBorgString()} --repo \"{P(repo.Path)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CreateBackupAsync(
        BorgRepository repo, string archiveName,
        IEnumerable<string> sourcePaths, CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo);
        var paths = string.Join(" ", sourcePaths.Select(p => $"\"{P(p)}\""));
        var rateLimit = repo.RateLimit > 0 ? $" --upload-ratelimit {repo.RateLimit}" : "";
        var args = $"create --progress{rateLimit} \"{P(repo.Path)}::{archiveName}\" {paths}";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> ListArchivesAsync(
        BorgRepository repo, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"rlist --json \"{P(repo.Path)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> InfoArchiveAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"info --json \"{P(repo.Path)}::{archiveName}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> ListArchiveContentsAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"list --json-lines \"{P(repo.Path)}::{archiveName}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> ExtractAsync(
        BorgRepository repo, string archiveName,
        string restorePath, IReadOnlyList<string>? paths = null,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo);
        var pathArgs = paths is { Count: > 0 }
            ? " " + string.Join(" ", paths.Select(p => $"\"{p}\""))
            : "";
        var args = $"extract --progress --noxattrs \"{P(repo.Path)}::{archiveName}\"{pathArgs}";
        return await RunCommandAsync(BorgBinary, args, env, P(restorePath), ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> DeleteArchiveAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"delete --repo \"{P(repo.Path)}\" \"{archiveName}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> PruneAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo);
        var retention = BuildPruneRetentionArgs(repo.PruneOptions);
        var args = $"prune --stats --list{retention} --repo \"{P(repo.Path)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> CheckAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"check --progress --repo \"{P(repo.Path)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> CompactAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"compact --progress --repo \"{P(repo.Path)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct, onStderrLine: onStderrLine);
    }

    public override async Task<BorgResult> InfoRepoAsync(
        BorgRepository repo,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"rinfo --json \"{P(repo.Path)}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }

    public override async Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(repo);
        var args = $"diff \"{P(repo.Path)}::{archive1}\" \"{archive2}\"";
        return await RunCommandAsync(BorgBinary, args, env, ct: ct);
    }
}
