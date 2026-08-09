using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace BorgMate.Infrastructure.Platform;

public class DirectorySizeCalculator(ILogger<DirectorySizeCalculator> logger)
{
    public (long totalSize, long fileCount) Calculate(
        IEnumerable<string> directories,
        IEnumerable<string> excludedPaths,
        CancellationToken ct,
        Action<long, long, long>? onProgress = null)
    {
        long totalSize = 0;
        long fileCount = 0;
        long dirCount = 0;
        var lastReport = Environment.TickCount64;
        var exclusions = new List<string>();
        foreach (var path in excludedPaths)
            exclusions.Add(NormalizePath(path));

        foreach (var dir in directories)
        {
            if (ct.IsCancellationRequested) break;
            if (!Directory.Exists(dir) || IsExcluded(dir, exclusions)) continue;

            var pending = new Stack<string>();
            pending.Push(dir);
            while (pending.TryPop(out var currentDirectory))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            if (IsExcluded(entry, exclusions)) continue;

                            if (Directory.Exists(entry))
                            {
                                dirCount++;
                                pending.Push(entry);
                            }
                            else
                            {
                                totalSize += new FileInfo(entry).Length;
                                fileCount++;
                            }

                            var now = Environment.TickCount64;
                            if (now - lastReport >= 500)
                            {
                                lastReport = now;
                                onProgress?.Invoke(dirCount, fileCount, totalSize);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Cannot access: {Entry}", entry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Cannot access directory: {Dir}", currentDirectory);
                }
            }
        }

        onProgress?.Invoke(dirCount, fileCount, totalSize);
        return (totalSize, fileCount);
    }

    private static bool IsExcluded(string path, IEnumerable<string> excludedPaths)
    {
        var normalizedPath = NormalizePath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var excludedPath in excludedPaths)
        {
            if (normalizedPath.Equals(excludedPath, comparison))
                return true;

            var prefix = Path.EndsInDirectorySeparator(excludedPath)
                ? excludedPath
                : excludedPath + Path.DirectorySeparatorChar;
            if (normalizedPath.StartsWith(prefix, comparison))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
