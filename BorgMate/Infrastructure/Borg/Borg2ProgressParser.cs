using System.Text.RegularExpressions;
using BorgMate.Runtime.Jobs;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Parses Borg 2 progress output.</summary>
public static partial class Borg2ProgressParser
{
    // Borg 2 create progress: "1.23 GB O  456 MB U  12345 N path"
    [GeneratedRegex(@"([\d.]+\s+\S+)\s+O\s+([\d.]+\s+\S+)\s+U\s+(\d+)\s+N")]
    private static partial Regex CreateProgressPattern();

    /// <summary>Parses a Borg 2 progress line and updates the job.</summary>
    public static void Update(BorgJob job, string line)
    {
        if (BorgProgressParser.UpdateCommon(job, line))
            return;

        var match = CreateProgressPattern().Match(line);
        if (match.Success)
            BorgProgressParser.UpdateCreateProgress(job, match.Groups[1].Value.Trim(), match.Groups[3].Value);
    }

    /// <summary>Parses Borg 2 create progress counters without updating UI state.</summary>
    internal static bool TryParseCreateProgress(string line, out long processedBytes, out long fileCount)
    {
        var match = CreateProgressPattern().Match(line);
        return BorgProgressParser.TryReadCreateProgress(match, 1, 3, out processedBytes, out fileCount);
    }
}
