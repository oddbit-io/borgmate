using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using BorgMate.Localization;
using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Journal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.ViewModels;

public partial class NotificationsViewModel : ViewModelBase
{
    private readonly IJournalService _journalService = null!;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<NotificationItem> Items { get; } = [];

    [ObservableProperty]
    private NotificationItem? _selectedItem;

    public IJournalService JournalService => _journalService;

    public NotificationsViewModel()
    {
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += (_, _) => RefreshAll();
        _refreshTimer.Start();
        Strings.Instance.PropertyChanged += (_, _) => RefreshAll();
    }

    public NotificationsViewModel(IJournalService journalService) : this()
    {
        _journalService = journalService;
        _journalService.Entries.CollectionChanged += OnEntriesChanged;
        RebuildList();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildList();
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (JournalEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= OnEntryPropertyChanged;
                var item = Items.FirstOrDefault(i => i.Entry == entry);
                if (item is not null)
                {
                    item.Detach();
                    Items.Remove(item);
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (JournalEntry entry in e.NewItems)
            {
                if (entry.IsFinished)
                    Items.Insert(0, new NotificationItem(entry));
                else
                    entry.PropertyChanged += OnEntryPropertyChanged;
            }
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(JournalEntry.Result)) return;
        if (sender is not JournalEntry entry || !entry.IsFinished) return;

        entry.PropertyChanged -= OnEntryPropertyChanged;

        // Add finished entry to the list if not already present
        if (Items.All(i => i.Entry != entry))
            Items.Insert(0, new NotificationItem(entry));
    }

    private void RebuildList()
    {
        foreach (var item in Items)
            item.Detach();
        Items.Clear();

        foreach (var entry in _journalService.Entries)
        {
            if (entry.IsFinished)
                Items.Add(new NotificationItem(entry));
            else
                entry.PropertyChanged += OnEntryPropertyChanged;
        }
    }

    private void RefreshAll()
    {
        foreach (var item in Items)
            item.Refresh();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ClearCompleted()
    {
        if (!await DialogHelper.ConfirmAsync(Strings.Get("ConfirmClearJournal")))
            return;
        SelectedItem = null;
        _journalService.ClearFinished();
    }
}
