using System;
using BorgMate.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;

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
    /// <summary>Localization keys indexed by JournalEventKind integer value.</summary>
    private static readonly string[] EventKindKeys =
    [
        "Notif.Backup",           // 0
        "Notif.Prune",            // 1
        "Notif.Check",            // 2
        "Notif.Compact",          // 3
        "Notif.Delete",           // 4
        "Notif.Create",           // 5
        "Notif.PassphraseFailed", // 6
        "Notif.Restore"           // 7
    ];

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

    public string Title
    {
        get
        {
            var key = EventKindKeys[(int)EventKind];
            return TitleArgs is { Length: > 0 } ? string.Format(Strings.Get(key), TitleArgs) : Strings.Get(key);
        }
    }

    public string ResultDisplay => Result switch
    {
        JournalResult.Running => Strings.Get("Job.Running"),
        JournalResult.Completed => Strings.Get("Job.Completed"),
        JournalResult.Failed => Strings.Get("Job.Failed"),
        JournalResult.Cancelled => Strings.Get("Job.Cancelled"),
        _ => ""
    };

    public bool IsCancelled => Result == JournalResult.Cancelled;
    public bool IsFailed => Result == JournalResult.Failed;
    public bool IsSuccess => Result == JournalResult.Completed;
    public bool IsFinished => Result is not JournalResult.Running;

    public string RelativeTime => (CompletedAt ?? StartedAt).Humanize(culture: Strings.Culture);
    public string StartedAtDisplay => StartedAt.ToString("d MMMM yyyy, HH:mm:ss", Strings.Culture);
    public string? CompletedAtDisplay => CompletedAt?.ToString("HH:mm:ss", Strings.Culture);

    public void Complete(JournalResult result, string? detail = null)
    {
        Result = result;
        CompletedAt = DateTime.Now;
        if (detail is not null)
            Detail = detail;
        OnPropertyChanged(nameof(ResultDisplay));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(RelativeTime));
        OnPropertyChanged(nameof(CompletedAtDisplay));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(RelativeTime));
        OnPropertyChanged(nameof(ResultDisplay));
        OnPropertyChanged(nameof(StartedAtDisplay));
        OnPropertyChanged(nameof(CompletedAtDisplay));
    }
}
