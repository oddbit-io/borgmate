using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;

namespace BorgMate.Runtime.Operations;

/// <summary>Loads capacity information for the storage backing a repository.</summary>
public class RepositoryStorageQuery
{
    private const int MaxDiagnosticLength = 1024;
    private readonly INetworkShareMounter _shareMounter;
    private readonly SshRepositoryTransport _sshTransport;
    private readonly ILogger<RepositoryStorageQuery> _logger;

    public RepositoryStorageQuery(
        INetworkShareMounter shareMounter,
        SshRepositoryTransport sshTransport,
        ILogger<RepositoryStorageQuery> logger)
    {
        _shareMounter = shareMounter;
        _sshTransport = sshTransport;
        _logger = logger;
    }

    public virtual async Task<StorageSpaceSnapshot?> GetAsync(
        BorgRepository repository,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (repository.LocationKind == BorgRepositoryLocationKind.SmbShare)
            {
                var location = SmbLocation.TryParse(repository.Path)
                               ?? throw new ArgumentException("Invalid SMB repository location.");
                return await _shareMounter.GetStorageSpaceAsync(location, ct);
            }

            if (repository.LocationKind == BorgRepositoryLocationKind.BorgUrl)
            {
                var url = string.IsNullOrWhiteSpace(repository.BorgUrl) ? repository.Path : repository.BorgUrl;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "file")
                    return DiskSpaceProbe.GetVolumeSpace(uri.LocalPath);
                if (!_sshTransport.UsesSsh(repository))
                    return null;
            }
            else if (repository.LocationKind == BorgRepositoryLocationKind.LocalPath && repository.IsLocal)
            {
                return DiskSpaceProbe.GetVolumeSpace(repository.Path);
            }

            if (!_sshTransport.UsesSsh(repository))
                return null;

            var target = _sshTransport.ResolveTarget(repository);
            var command = $"df -P -k {SshRepositoryTransport.QuoteRemoteArgument(target.RepositoryPath)}";
            var result = await _sshTransport.RunAsync(repository, target, command, ct);
            if (result.WasCancelled)
                throw new OperationCanceledException(ct);
            if (!result.Success)
            {
                LogFailure(repository, result.ExitCode, result.ErrorMessage);
                return null;
            }

            var snapshot = DiskSpaceProbe.ParseDfOutput(result.StandardOutput);
            if (snapshot is null)
                LogFailure(repository, result.ExitCode, "df returned unrecognized output");
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   DiskSpaceQueryException or NetworkShareMountException or
                                   CredentialsRequiredException or ArgumentException)
        {
            LogFailure(repository, (ex as DiskSpaceQueryException)?.ExitCode, ex.Message, ex);
            return null;
        }
    }

    private void LogFailure(
        BorgRepository repository,
        int? exitCode,
        string detail,
        Exception? exception = null)
    {
        var safeDetail = BorgCommandRedactor.Redact(detail);
        if (safeDetail.Length > MaxDiagnosticLength)
            safeDetail = safeDetail[..MaxDiagnosticLength];

        _logger.LogWarning(
            exception,
            "Failed to query repository storage: Name={RepositoryName}, Location={RepositoryLocation}, " +
            "LocationKind={LocationKind}, BorgVersion={BorgVersion}, ExitCode={ExitCode}, " +
            "ExceptionType={ExceptionType}, Detail={Detail}",
            repository.Name,
            BorgCommandRedactor.RedactRepositoryLocation(repository),
            repository.LocationKind,
            repository.BorgVersion,
            exitCode,
            exception?.GetType().Name,
            safeDetail);
    }
}
