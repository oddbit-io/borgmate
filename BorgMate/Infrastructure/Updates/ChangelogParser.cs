using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BorgMate.Infrastructure.Updates;

/// <summary>
///     Parses <c>changelog-v2.json</c> and selects entries by SemVer precedence, so build metadata never
///     affects which versions are shown.
/// </summary>
public static class ChangelogParser
{
    /// <summary>Returns the entries newer than <paramref name="currentVersion" /> up to <paramref name="targetVersion" />.</summary>
    /// <exception cref="JsonException">The changelog is malformed.</exception>
    public static List<ChangelogEntry> ParseBetween(string json, string currentVersion, string targetVersion)
    {
        var current = SemVerParser.Parse(currentVersion);
        var target = SemVerParser.Parse(targetVersion);
        return Parse(json)
            .Where(entry => SemVerParser.Parse(entry.Version) is var version && version > current && version <= target)
            .ToList();
    }

    /// <summary>Returns every entry in file order, newest first.</summary>
    /// <exception cref="JsonException">The changelog is malformed.</exception>
    public static List<ChangelogEntry> Parse(string json)
    {
        var changelog = JsonSerializer.Deserialize<List<ChangelogEntry>>(json, JsonSerializerOptions.Web)
                        ?? throw new JsonException("Changelog is null.");

        foreach (var entry in changelog)
        {
            if (string.IsNullOrWhiteSpace(entry.Version))
                throw new JsonException("Changelog version is empty.");
            if (entry.Changes is null)
                throw new JsonException($"Changelog changes are null for version {entry.Version}.");
        }

        return changelog;
    }
}
