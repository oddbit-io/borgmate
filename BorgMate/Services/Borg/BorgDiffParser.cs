using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BorgMate.Models;

namespace BorgMate.Services.Borg;

/// <summary>
/// Parses borg diff text output into a dictionary of changed paths with their change kind.
/// </summary>
public static partial class BorgDiffParser
{
    // Matches content size changes like " +77 B    -66 B path/to/file"
    [GeneratedRegex(@"^\s+\+[\d.]+\s+\S+\s+-[\d.]+\s+\S+\s+(.+)$")]
    private static partial Regex ContentChangePattern();

    // Matches "added       2.5 kB path/to/file"
    [GeneratedRegex(@"^added\s+[\d.]+\s+\S+\s+(.+)$")]
    private static partial Regex AddedPattern();

    public static Dictionary<string, FileChangeKind> ParseChangedPaths(string output)
    {
        var result = new Dictionary<string, FileChangeKind>(StringComparer.Ordinal);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var addedMatch = AddedPattern().Match(line);
            if (addedMatch.Success)
            {
                result[addedMatch.Groups[1].Value.TrimEnd()] = FileChangeKind.Added;
                continue;
            }

            var contentMatch = ContentChangePattern().Match(line);
            if (contentMatch.Success)
            {
                result[contentMatch.Groups[1].Value.TrimEnd()] = FileChangeKind.Modified;
            }
        }

        return result;
    }
}
