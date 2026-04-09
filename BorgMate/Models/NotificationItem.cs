using System.ComponentModel;
using BorgMate.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Models;

/// <summary>
/// Wraps a JournalEntry for the Notifications page.
/// </summary>
public partial class NotificationItem : ObservableObject
{
    public JournalEntry Entry { get; }

    public string Title => Entry.Title;
    public string? Detail => Entry.Detail;
    public string? RepositoryName => Entry.RepositoryName;
    public string RelativeTime => Entry.RelativeTime;
    public string? ResultDisplay => Entry.ResultDisplay;

    public bool IsCancelled => Entry.IsCancelled;
    public bool IsFailed => Entry.IsFailed;
    public bool IsSuccess => Entry.IsSuccess;

    public JournalResult DisplayResult => Entry.Result;

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
                OnPropertyChanged(nameof(ResultDisplay));
                OnPropertyChanged(nameof(DisplayResult));
                break;
            case nameof(JournalEntry.Detail):
                OnPropertyChanged(nameof(Detail));
                break;
        }
    }

    public void Refresh()
    {
        Entry.Refresh();
        OnPropertyChanged(nameof(RelativeTime));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ResultDisplay));
    }

    public void Detach()
    {
        Entry.PropertyChanged -= OnEntryPropertyChanged;
    }
}
