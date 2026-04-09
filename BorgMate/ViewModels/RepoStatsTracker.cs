using System;
using System.Text.Json;
using BorgMate.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BorgMate.ViewModels;

/// <summary>
/// Holds raw and culture-formatted repository statistics for AXAML binding.
/// Raw values are stored for re-formatting on language change.
/// </summary>
public partial class RepoStatsTracker : ObservableObject
{
    private long? _rawTotalSize;
    private long? _rawTotalChunks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStats))]
    private string? _totalSize;

    [ObservableProperty]
    private string? _totalChunks;

    [ObservableProperty]
    private bool _isLoading;

    public bool HasStats => TotalSize is not null;

    public void Clear()
    {
        _rawTotalSize = null;
        _rawTotalChunks = null;
        TotalSize = null;
        TotalChunks = null;
        OnPropertyChanged(nameof(HasStats));
    }

    /// <summary>Re-formats raw values with current Strings.Culture. Called on language change.</summary>
    public void Reformat()
    {
        if (_rawTotalSize is { } size)
            TotalSize = Strings.FormatBytes(size);
        if (_rawTotalChunks is { } chunks)
            TotalChunks = chunks.ToString("N0", Strings.Culture);
    }

    /// <summary>
    /// Parses "borg info --json" output. Reads cache.stats.unique_csize and total_chunks.
    /// </summary>
    public bool ParseRepoInfo(string json, ILogger? logger = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("cache", out var cache) && cache.TryGetProperty("stats", out var stats))
            {
                if (stats.TryGetProperty("unique_csize", out var uniqueCsize))
                    _rawTotalSize = uniqueCsize.GetInt64();
                if (stats.TryGetProperty("total_chunks", out var totalChunks))
                    _rawTotalChunks = totalChunks.GetInt64();
            }

            Reformat();
            OnPropertyChanged(nameof(HasStats));
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse repo info JSON");
            return false;
        }
    }
}
