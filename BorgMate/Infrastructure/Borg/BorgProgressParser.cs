using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using BorgMate.Runtime.Jobs;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Common progress parsing helpers shared by Borg version-specific parsers.</summary>
public static partial class BorgProgressParser
{
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%")]
    private static partial Regex PercentPattern();

    /// <summary>Parses common percentage-based progress lines.</summary>
    public static bool UpdateCommon(BorgJob job, string line)
    {
        var pctMatch = PercentPattern().Match(line);
        if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var pct))
        {
            // Borg check has two phases: "Checking segments" (~95% of time) then "Checking archives" (~5%)
            if (line.Contains("checking archives", StringComparison.OrdinalIgnoreCase))
                pct = 95 + pct * 0.05;
            else if (line.Contains("checking segments", StringComparison.OrdinalIgnoreCase))
                pct *= 0.95;

            var label = job.ProgressLabel ?? "";
            job.RecordProgress(pct);
            Dispatcher.UIThread.Post(() =>
            {
                job.Progress = pct;
                job.StatusMessage = label;
            });
            return true;
        }

        return false;
    }

    /// <summary>Applies a parsed create-progress counter to the job.</summary>
    public static void UpdateCreateProgress(BorgJob job, string originalSizeText, string fileCountText)
    {
        var processedBytes = ParseBorgSize(originalSizeText);
        long.TryParse(fileCountText, out var nfiles);
        double? progress = null;
        if (job.TotalSize > 0 && processedBytes > 0)
            progress = Math.Min(100.0, processedBytes * 100.0 / job.TotalSize);

        var filesTotal = job.TotalFileCount > 0
            ? job.TotalFileCount.ToString("N0", Strings.Culture)
            : "?";
        var filesProcessed = nfiles.ToString("N0", Strings.Culture);

        var sizeTotal = job.TotalSize > 0 ? Strings.FormatBytes(job.TotalSize) : "?";
        var sizeProcessed = job.TotalSize > 0
            ? Strings.FormatBytesInUnit(processedBytes, job.TotalSize)
            : Strings.FormatBytes(processedBytes);

        var msg = string.Format(Strings.Get("Status.BackupProgress"),
            filesProcessed, filesTotal, sizeProcessed, sizeTotal);

        if (progress.HasValue)
            job.RecordProgress(progress.Value);
        Dispatcher.UIThread.Post(() =>
        {
            job.StatusMessage = msg;
            if (progress.HasValue)
                job.Progress = progress.Value;
        });
    }

    /// <summary>Reads matched create-progress groups without updating UI state.</summary>
    internal static bool TryReadCreateProgress(
        Match match,
        int originalSizeGroup,
        int fileCountGroup,
        out long processedBytes,
        out long fileCount)
    {
        processedBytes = 0;
        fileCount = 0;

        if (!match.Success)
            return false;

        processedBytes = ParseBorgSize(match.Groups[originalSizeGroup].Value.Trim());
        long.TryParse(match.Groups[fileCountGroup].Value, out fileCount);
        return true;
    }

    /// <summary>
    ///     Parses borg size strings like "1.23 GB", "456 MB", "789 kB" into bytes.
    ///     Returns 0 on parse failure.
    /// </summary>
    internal static long ParseBorgSize(string sizeStr)
    {
        var parts = sizeStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return 0;
        if (!double.TryParse(parts[0], CultureInfo.InvariantCulture, out var value)) return 0;

        var unit = parts[1].ToUpperInvariant();
        var multiplier = unit switch
        {
            "B" => 1L,
            "KB" => 1000L,
            "MB" => 1_000_000L,
            "GB" => 1_000_000_000L,
            "TB" => 1_000_000_000_000L,
            "KIB" => 1024L,
            "MIB" => 1024L * 1024,
            "GIB" => 1024L * 1024 * 1024,
            "TIB" => 1024L * 1024 * 1024 * 1024,
            _ => 0L
        };

        return (long)(value * multiplier);
    }
}
