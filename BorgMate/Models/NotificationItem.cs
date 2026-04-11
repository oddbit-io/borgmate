using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Models;

/// <summary>Wraps a JournalEntry for the Notifications page.</summary>
public partial class NotificationItem : ObservableObject
{
    public JournalEntry Entry { get; }
    public string? Detail => Entry.Detail;
    public string? RepositoryName => Entry.RepositoryName;
    public bool IsCancelled => Entry.IsCancelled;
    public bool IsFailed => Entry.IsFailed;
    public bool IsSuccess => Entry.IsSuccess;
    public JournalResult DisplayResult => Entry.Result;

    /// <summary>Time to show as relative ("3 minutes ago"): CompletedAt if finished, StartedAt otherwise.</summary>
    public DateTime DisplayTime => Entry.CompletedAt ?? Entry.StartedAt;

    public NotificationItem(JournalEntry entry)
    {
        Entry = entry;
        entry.PropertyChanged += OnEntryPropertyChanged;
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(JournalEntry.Result):
                OnPropertyChanged(nameof(IsCancelled));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(DisplayResult));
                break;
            case nameof(JournalEntry.Detail):
                OnPropertyChanged(nameof(Detail));
                break;
            case nameof(JournalEntry.CompletedAt):
                OnPropertyChanged(nameof(DisplayTime));
                break;
        }
    }

    public void Refresh()
    {
        Entry.Refresh();
        OnPropertyChanged(nameof(DisplayTime));
        OnPropertyChanged(nameof(Entry));
        OnPropertyChanged(nameof(DisplayResult));
    }

    public void Detach()
    {
        Entry.PropertyChanged -= OnEntryPropertyChanged;
    }
}
