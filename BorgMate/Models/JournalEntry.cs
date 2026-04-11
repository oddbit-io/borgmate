using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Models;

/// <summary>Entry lifecycle: created as Running at operation start, updated to final state via Complete().</summary>
public enum JournalResult { Running, Completed, Failed, Cancelled }

/// <summary>
/// Borg operation type. Explicit integer values are stored in the SQLite journal — do not reorder.
/// </summary>
public enum JournalEventKind
{
    Backup = 0,
    Prune = 1,
    Check = 2,
    Compact = 3,
    Delete = 4,
    Create = 5,
    PassphraseFailed = 6,
    Restore = 7
}

public partial class JournalEntry : ObservableObject
{
    public long Id { get; set; }
    public JournalEventKind EventKind { get; }

    /// <summary>Format arguments for the localized title template (e.g. archive name, repo name).</summary>
    public object[]? TitleArgs { get; }
    public string? RepositoryName { get; }
    public DateTime StartedAt { get; }

    [ObservableProperty]
    private JournalResult _result;

    [ObservableProperty]
    private DateTime? _completedAt;

    [ObservableProperty]
    private string? _detail;

    public JournalEntry(JournalEventKind eventKind, object[]? titleArgs = null, string? detail = null,
        string? repositoryName = null, DateTime? startedAt = null, DateTime? completedAt = null,
        JournalResult result = JournalResult.Running, long id = 0)
    {
        Id = id;
        EventKind = eventKind;
        TitleArgs = titleArgs;
        _detail = detail;
        RepositoryName = repositoryName;
        StartedAt = startedAt ?? DateTime.Now;
        _completedAt = completedAt;
        _result = result;
    }

    public bool IsCancelled => Result == JournalResult.Cancelled;
    public bool IsFailed => Result == JournalResult.Failed;
    public bool IsSuccess => Result == JournalResult.Completed;
    public bool IsFinished => Result is not JournalResult.Running;

    public void Complete(JournalResult result, string? detail = null)
    {
        Result = result;
        CompletedAt = DateTime.Now;
        if (detail is not null)
            Detail = detail;
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsFinished));
    }

    /// <summary>Forces re-evaluation of display converters (e.g. relative time updates).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(EventKind));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(StartedAt));
        OnPropertyChanged(nameof(CompletedAt));
    }
}
