using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;

namespace BorgMate.Infrastructure.Platform;

/// <summary>Mounts SMB shares before borg operations and resolves their local repository paths.</summary>
public interface INetworkShareMounter
{
    /// <summary>
    ///     Ensures the share is mounted, prompting for credentials when needed, and returns the mountpoint.
    ///     Throws <see cref="NetworkShareMountException" /> with a user-facing message on failure and
    ///     <see cref="System.OperationCanceledException" /> when the user cancels the password prompt.
    /// </summary>
    Task<string> EnsureMountedAsync(SmbLocation location, CancellationToken ct = default);

    /// <summary>
    ///     Returns the repository path (mountpoint plus path in share) for a share mounted earlier
    ///     in this session via <see cref="EnsureMountedAsync" />. On Windows the returned path is
    ///     WSL-native and must not be converted again.
    /// </summary>
    string GetMountedPath(SmbLocation location);
}
