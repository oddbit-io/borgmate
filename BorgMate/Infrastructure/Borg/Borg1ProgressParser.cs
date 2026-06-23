using System.Text.RegularExpressions;
using BorgMate.Runtime.Jobs;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Parses Borg 1 progress output.</summary>
public static partial class Borg1ProgressParser
{
    // Borg 1 create progress: "1.23 GB O  456 MB C  456 MB D  12345 N path"
    [GeneratedRegex(@"([\d.]+\s+\S+)\s+O\s+([\d.]+\s+\S+)\s+C\s+([\d.]+\s+\S+)\s+D\s+(\d+)\s+N")]
    private static partial Regex CreateProgressPattern();

    /// <summary>Parses a Borg 1 progress line and updates the job.</summary>
    public static void Update(BorgJob job, string line)
    {
        if (BorgProgressParser.UpdateCommon(job, line))
            return;

        var match = CreateProgressPattern().Match(line);
        if (match.Success)
            BorgProgressParser.UpdateCreateProgress(job, match.Groups[1].Value.Trim(), match.Groups[4].Value);
    }

    /// <summary>Parses Borg 1 create progress counters without updating UI state.</summary>
    internal static bool TryParseCreateProgress(string line, out long processedBytes, out long fileCount)
    {
        var match = CreateProgressPattern().Match(line);
        return BorgProgressParser.TryReadCreateProgress(match, 1, 4, out processedBytes, out fileCount);
    }
}
