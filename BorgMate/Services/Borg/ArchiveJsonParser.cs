using System;
using System.Collections.Generic;
using System.Text.Json;
using BorgMate.Models;

namespace BorgMate.Services.Borg;

/// <summary>
/// Parses borg JSON output for archive lists and archive detail info.
/// </summary>
internal static class ArchiveJsonParser
{
    /// <summary>
    /// Parses borg JSON output with "archives" array. Falls back to treating
    /// non-JSON lines as plain archive names when JSON parsing fails.
    /// </summary>
    public static List<BorgArchive> ParseArchiveList(string json)
    {
        var archives = new List<BorgArchive>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("archives", out var archivesArray))
                return archives;

            foreach (var item in archivesArray.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString() ?? "";
                var time = item.TryGetProperty("time", out var timeProp)
                    ? DateTime.TryParse(timeProp.GetString(), out var dt) ? dt : DateTime.MinValue
                    : item.TryGetProperty("start", out var startProp)
                        ? DateTime.TryParse(startProp.GetString(), out var st) ? st : DateTime.MinValue
                        : DateTime.MinValue;

                archives.Add(new BorgArchive(name, time));
            }
        }
        catch
        {
            foreach (var line in json.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith("{") && !line.StartsWith("["))
                    archives.Add(new BorgArchive(line, DateTime.MinValue));
            }
        }
        return archives;
    }

    /// <summary>Parses "borg info --json" for a single archive. Returns (originalSize, fileCount).</summary>
    public static (long OriginalSize, long FileCount)? ParseArchiveDetail(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("archives", out var archives)) return null;

            foreach (var archive in archives.EnumerateArray())
            {
                if (!archive.TryGetProperty("stats", out var stats)) continue;
                var origSize = stats.TryGetProperty("original_size", out var orig) ? orig.GetInt64() : 0;
                var fileCount = stats.TryGetProperty("nfiles", out var nfiles) ? nfiles.GetInt64() : 0;
                return (origSize, fileCount);
            }
        }
        catch { }
        return null;
    }

    /// <summary>Parses "borg info --json" output. Returns (unique_csize, total_chunks).</summary>
    public static (long? Size, long? Chunks)? ParseRepoStats(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            long? size = null, chunks = null;

            if (doc.RootElement.TryGetProperty("cache", out var cache) &&
                cache.TryGetProperty("stats", out var stats))
            {
                if (stats.TryGetProperty("unique_csize", out var cs)) size = cs.GetInt64();
                if (stats.TryGetProperty("total_chunks", out var tc)) chunks = tc.GetInt64();
            }

            return (size, chunks);
        }
        catch { return null; }
    }
}
