using System.Threading;
using System.Threading.Tasks;
using BorgMate.Runtime.Orchestration;
using OneOf;

namespace BorgMate.Runtime.Operations;

/// <summary>Runs repository editor verification operations.</summary>
public interface IRepositoryVerificationOperationRunner
{
    /// <summary>Detects the configured borg version.</summary>
    Task<OneOf<VersionDetectionResult, OperationError>> DetectVersionAsync(
        VersionDetectionOperationOptions options, CancellationToken ct = default);

    /// <summary>Initializes and verifies a new repository candidate.</summary>
    Task<OneOf<RepositoryVerificationResult, OperationError>> VerifyNewRepositoryAsync(
        RepositoryVerificationOperationOptions options, CancellationToken ct = default);

    /// <summary>Verifies reachability and credentials for an existing repository.</summary>
    Task<OneOf<RepositoryVerificationResult, OperationError>> VerifyExistingRepositoryAsync(
        RepositoryVerificationOperationOptions options, CancellationToken ct = default);

    /// <summary>Checks a remote borg binary path.</summary>
    Task<OneOf<RemotePathCheckResult, OperationError>> CheckRemotePathAsync(
        RemotePathCheckOperationOptions options, CancellationToken ct = default);

    /// <summary>Prepares an SSH key for runtime use when the platform requires it.</summary>
    Task<OneOf<SshKeyPreparationResult, OperationError>> PrepareSshKeyAsync(
        SshKeyPreparationOperationOptions options, CancellationToken ct = default);
}