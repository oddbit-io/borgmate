using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services;

public class DirectorySizeCalculator(ILogger<DirectorySizeCalculator> logger)
{
    public (long totalSize, long fileCount) Calculate(
        IEnumerable<string> directories, CancellationToken ct, Action<long, long, long>? onProgress = null)
    {
        long totalSize = 0;
        long fileCount = 0;
        long dirCount = 0;
        var lastReport = Environment.TickCount64;
        foreach (var dir in directories)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        if (Directory.Exists(entry))
                        {
                            dirCount++;
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
                    catch (Exception ex) { logger.LogDebug(ex, "Cannot access: {Entry}", entry); }
                }
            }
            catch (Exception ex) { logger.LogDebug(ex, "Cannot access directory: {Dir}", dir); }
        }
        onProgress?.Invoke(dirCount, fileCount, totalSize);
        return (totalSize, fileCount);
    }
}
