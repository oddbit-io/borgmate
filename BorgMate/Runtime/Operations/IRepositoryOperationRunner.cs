using System.Threading;
using System.Threading.Tasks;
using BorgMate.Runtime.Orchestration;
using OneOf;

namespace BorgMate.Runtime.Operations;

/// <summary>Runs repository-changing borg operations through the runtime lifecycle.</summary>
public interface IRepositoryOperationRunner
{
    /// <summary>Creates a backup archive.</summary>
    Task<OneOf<BackupResult, OperationError>> BackupAsync(
        BackupOperationOptions options, CancellationToken ct = default);

    /// <summary>Prunes archives according to repository retention options.</summary>
    Task<OneOf<PruneResult, OperationError>> PruneAsync(
        PruneOperationOptions options, CancellationToken ct = default);

    /// <summary>Checks repository consistency.</summary>
    Task<OneOf<CheckResult, OperationError>> CheckAsync(
        CheckOperationOptions options, CancellationToken ct = default);

    /// <summary>Compacts repository storage.</summary>
    Task<OneOf<CompactResult, OperationError>> CompactAsync(
        CompactOperationOptions options, CancellationToken ct = default);

    /// <summary>Restores an archive or selected archive paths.</summary>
    Task<OneOf<RestoreResult, OperationError>> RestoreAsync(
        RestoreOperationOptions options, CancellationToken ct = default);

    /// <summary>Deletes an archive from a repository.</summary>
    Task<OneOf<DeleteArchiveResult, OperationError>> DeleteArchiveAsync(
        DeleteArchiveOperationOptions options, CancellationToken ct = default);

    /// <summary>Changes a repository encryption passphrase.</summary>
    Task<OneOf<ChangePassphraseResult, OperationError>> ChangePassphraseAsync(
        ChangePassphraseOperationOptions options, CancellationToken ct = default);

    /// <summary>Clears repository error state owned outside the persisted model.</summary>
    void ClearRepositoryError(ClearRepositoryErrorOptions options);
}