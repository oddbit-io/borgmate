using System;
using System.Globalization;
using System.IO;
using BorgMate.Domain.Repositories;

namespace BorgMate.Infrastructure.Platform;

/// <summary>Reports capacity of the volume containing a local path.</summary>
public static class DiskSpaceProbe
{
    /// <summary>
    ///     Returns the capacity of the volume containing <paramref name="path" />.
    /// </summary>
    public static StorageSpaceSnapshot GetVolumeSpace(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Repository path does not exist: {path}");

        var root = OperatingSystem.IsWindows() ? Path.GetPathRoot(Path.GetFullPath(path)) : path;
        if (string.IsNullOrEmpty(root))
            throw new DriveNotFoundException($"Cannot resolve the volume for repository path: {path}");

        var drive = new DriveInfo(root);
        var snapshot = new StorageSpaceSnapshot
        {
            AvailableBytes = drive.AvailableFreeSpace,
            CapacityBytes = drive.TotalSize
        };
        return snapshot.IsValid
            ? snapshot
            : throw new DiskSpaceQueryException($"Volume returned invalid capacity values for path: {path}");
    }

    /// <summary>
    ///     Parses POSIX <c>df -P -k</c> output ("1024-blocks Used Available ...") into
    ///     free and total bytes, or null when the output is not recognized.
    /// </summary>
    public static StorageSpaceSnapshot? ParseDfOutput(string output)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 1; i--)
        {
            var tokens = lines[i].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 4 ||
                !long.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalKb) ||
                !long.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var usedKb) ||
                !long.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var freeKb) ||
                totalKb <= 0 || freeKb < 0 || usedKb < 0 || usedKb > totalKb ||
                totalKb > long.MaxValue / 1024 || freeKb > long.MaxValue / 1024)
                continue;

            var snapshot = new StorageSpaceSnapshot
            {
                CapacityBytes = totalKb * 1024,
                AvailableBytes = freeKb * 1024
            };
            if (snapshot.IsValid)
                return snapshot;
        }

        return null;
    }
}
