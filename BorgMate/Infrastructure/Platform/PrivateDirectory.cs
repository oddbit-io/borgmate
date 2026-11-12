using System;
using System.IO;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Creates directories that only their owner can enter. On Linux and macOS a new directory follows
///     the umask (usually 0755), which lets other local users read the files inside.
/// </summary>
public static class PrivateDirectory
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Creates <paramref name="path" /> if needed and restricts it, including one created by an earlier version.</summary>
    public static string Ensure(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return path;
        }

        Directory.CreateDirectory(path, OwnerOnly);
        File.SetUnixFileMode(path, OwnerOnly);
        return path;
    }
}
