using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Domain.Journal;

public partial class JournalEntry : ObservableObject
{
    [ObservableProperty]
    private DateTime? _completedAt;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private JournalResult _result;

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

    public long Id { get; set; }
    public JournalEventKind EventKind { get; }

    /// <summary>Format arguments for the localized title template (e.g. archive name, repo name).</summary>
    public object[]? TitleArgs { get; }

    public string? RepositoryName { get; }
    public DateTime StartedAt { get; }

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