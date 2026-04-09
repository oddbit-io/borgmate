using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Models;

namespace BorgMate.Services.Borg;

/// <summary>
/// Abstracts borg CLI version differences. Implemented by Borg1Service and Borg2Service.
/// All methods return BorgResult with exit code, stdout (often JSON), and stderr.
/// </summary>
public interface IBorgService
{
    Task<BorgResult> GetVersionAsync(CancellationToken ct = default);

    Task<BorgResult> InitAsync(
        BorgRepository repo,
        CancellationToken ct = default);

    Task<BorgResult> CreateBackupAsync(
        BorgRepository repo, string archiveName,
        IEnumerable<string> sourcePaths, CancellationToken ct = default,
        Action<string>? onStderrLine = null);

    /// <summary>Returns JSON archive list in stdout (parsed by ArchiveJsonParser).</summary>
    Task<BorgResult> ListArchivesAsync(
        BorgRepository repo, CancellationToken ct = default);

    /// <summary>Returns per-archive stats (original size, file count) as JSON in stdout.</summary>
    Task<BorgResult> InfoArchiveAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default);

    /// <summary>Returns JSON-lines per-file metadata (path, type, size, mtime) in stdout.</summary>
    Task<BorgResult> ListArchiveContentsAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default);

    /// <summary>Extracts archive to restorePath. When paths is non-null, restores only those entries.</summary>
    Task<BorgResult> ExtractAsync(
        BorgRepository repo, string archiveName,
        string restorePath, IReadOnlyList<string>? paths = null,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null);

    Task<BorgResult> DeleteArchiveAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default);

    Task<BorgResult> PruneAsync(
        BorgRepository repo,
        CancellationToken ct = default);

    Task<BorgResult> CheckAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null);

    Task<BorgResult> InfoRepoAsync(
        BorgRepository repo,
        CancellationToken ct = default);

    Task<BorgResult> CompactAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null);

    /// <summary>Returns text diff output (parsed by BorgDiffParser).</summary>
    Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default);

    Task<BorgResult> CheckRemotePathAsync(
        string host, string remotePath,
        string? sshKeyPath = null, CancellationToken ct = default);
}
