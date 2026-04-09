using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using BorgMate.Localization;
using BorgMate.Models;

namespace BorgMate.Services.Borg;

/// <summary>
/// Parses borg stderr progress output and updates BorgJob.Progress.
/// Extract outputs "Extracting: 45%" style lines → determinate progress.
/// Create outputs " 1.23 GB O  456 MB C  456 MB D" → status message update + progress %.
/// </summary>
public static partial class BorgProgressParser
{
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%")]
    private static partial Regex PercentPattern();

    // Matches borg create progress: "1.23 GB O  456 MB C  456 MB D  12345 N path"
    [GeneratedRegex(@"([\d.]+\s+\S+)\s+O\s+([\d.]+\s+\S+)\s+C\s+([\d.]+\s+\S+)\s+D\s+(\d+)\s+N")]
    private static partial Regex BytesPattern();

    /// <summary>
    /// Parses a borg stderr line and updates job progress.
    /// Handles percentage lines (extract/check) and byte counter lines (create).
    /// For percentage-based operations (check, compact), set BorgJob.ProgressLabel before starting.
    /// </summary>
    public static void Update(BorgJob job, string line)
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
            return;
        }

        var bytesMatch = BytesPattern().Match(line);
        if (bytesMatch.Success)
        {
            var original = bytesMatch.Groups[1].Value.Trim();
            long.TryParse(bytesMatch.Groups[4].Value, out var nfiles);

            var processedBytes = ParseBorgSize(original);
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
    }

    /// <summary>
    /// Parses borg size strings like "1.23 GB", "456 MB", "789 kB" into bytes.
    /// Returns 0 on parse failure.
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
