using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Runtime.Jobs;

namespace BorgMate.Infrastructure.Borg;

/// <summary>
///     Abstracts Borg CLI version differences, supported features, and command execution.
/// </summary>
public interface IBorgService
{
    Task<BorgResult> GetVersionAsync(CancellationToken ct = default);

    /// <summary>Validates that the configured binary is compatible with this service.</summary>
    Task<BorgCliVersionValidationResult> ValidateCliVersionAsync(CancellationToken ct = default);

    Task<bool> SupportsFeatureAsync(BorgCliFeature feature, CancellationToken ct = default);

    /// <summary>Parses service-specific borg progress output for a running job.</summary>
    void UpdateProgress(BorgJob job, string line);

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
        string restorePath, BorgExtractOptions extractOptions,
        IReadOnlyList<string>? paths = null,
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

    /// <summary>
    ///     Rotates the repository's encryption passphrase. The current passphrase is
    ///     provided by the passphrase provider; the new one is passed explicitly.
    /// </summary>
    Task<BorgResult> ChangePassphraseAsync(
        BorgRepository repo, string newPassphrase,
        CancellationToken ct = default);

    /// <summary>Returns text diff output (parsed by BorgDiffParser).</summary>
    Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default);

}
