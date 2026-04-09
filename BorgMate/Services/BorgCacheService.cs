using System.Collections.Generic;
using BorgMate.Models;

namespace BorgMate.Services;

/// <summary>
/// Centralizes all borg data caches. Singleton, injected into ViewModels.
/// Cleared when main window hides to tray; invalidated per-repo after modifying operations.
/// </summary>
public class BorgCacheService
{
    private readonly Dictionary<string, List<BorgArchive>> _archiveLists = new();
    private readonly Dictionary<string, ArchiveDetail> _archiveDetails = new();
    private readonly Dictionary<string, string> _archiveContents = new();
    private readonly Dictionary<string, Dictionary<string, FileChangeKind>> _diffs = new();

    public record ArchiveDetail(long OriginalSize, long FileCount);

    // --- Archive list (per repo) ---

    public List<BorgArchive>? GetArchiveList(string repoPath) =>
        _archiveLists.GetValueOrDefault(repoPath);

    public void SetArchiveList(string repoPath, List<BorgArchive> archives) =>
        _archiveLists[repoPath] = archives;

    // --- Archive detail (per repo::archive) ---

    public ArchiveDetail? GetArchiveDetail(string repoPath, string archiveName) =>
        _archiveDetails.GetValueOrDefault($"{repoPath}::{archiveName}");

    public void SetArchiveDetail(string repoPath, string archiveName, ArchiveDetail detail) =>
        _archiveDetails[$"{repoPath}::{archiveName}"] = detail;

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

    /// <summary>
    /// Clears all cached data for a specific repository.
    /// Called after modifying operations (backup, prune, delete, compact).
    /// </summary>
    public void InvalidateRepo(string repoPath)
    {
        _archiveLists.Remove(repoPath);
        RemoveByPrefix(_archiveDetails, $"{repoPath}::");
        RemoveByPrefix(_archiveContents, $"{repoPath}::");
        RemoveByPrefix(_diffs, $"{repoPath}::");
    }

    /// <summary>
    /// Clears all caches. Called when main window hides to tray.
    /// </summary>
    public void ClearAll()
    {
        _archiveLists.Clear();
        _archiveDetails.Clear();
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
