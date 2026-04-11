using System.Collections.Generic;
using BorgMate.Models;

namespace BorgMate.Services;

/// <summary>Per-archive query result cache (contents, diffs).</summary>
public class BorgCacheService
{
    private readonly Dictionary<string, string> _archiveContents = new();
    private readonly Dictionary<string, Dictionary<string, FileChangeKind>> _diffs = new();

    // --- Archive contents / file listing (per repo::archive) ---

    public string? GetArchiveContents(string repoPath, string archiveName) =>
        _archiveContents.GetValueOrDefault($"{repoPath}::{archiveName}");

    public void SetArchiveContents(string repoPath, string archiveName, string stdout) =>
        _archiveContents[$"{repoPath}::{archiveName}"] = stdout;

    // --- Diff between archives ---

    public Dictionary<string, FileChangeKind>? GetDiff(string repoPath, string archive1, string archive2) =>
        _diffs.GetValueOrDefault($"{repoPath}::{archive1}..{archive2}");

    public void SetDiff(string repoPath, string archive1, string archive2, Dictionary<string, FileChangeKind> diff) =>
        _diffs[$"{repoPath}::{archive1}..{archive2}"] = diff;

    // --- Invalidation ---

    public void InvalidateRepo(string repoPath)
    {
        RemoveByPrefix(_archiveContents, $"{repoPath}::");
        RemoveByPrefix(_diffs, $"{repoPath}::");
    }

    public void ClearAll()
    {
        _archiveContents.Clear();
        _diffs.Clear();
    }

    private static void RemoveByPrefix<T>(Dictionary<string, T> dict, string prefix)
    {
        var keys = new List<string>();
        foreach (var key in dict.Keys)
            if (key.StartsWith(prefix))
                keys.Add(key);
        foreach (var key in keys)
            dict.Remove(key);
    }
}
