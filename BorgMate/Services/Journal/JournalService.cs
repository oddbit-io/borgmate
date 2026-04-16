using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using BorgMate.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Services.Journal;

public partial class JournalService : ObservableObject, IJournalService
{
    private const int MaxEntries = 200;
    private readonly JournalDb _db = new();
    private readonly Notifications.NotificationService? _notifications;
    private readonly AppSettings? _settings;

    [ObservableProperty]
    private int _unreadCount;

    public bool IsActive { get; set; }

    public ObservableCollection<JournalEntry> Entries { get; } = [];

    public JournalService() { }

    public JournalService(Notifications.NotificationService notifications, AppSettings settings)
    {
        _notifications = notifications;
        _settings = settings;
    }

    public void Load()
    {
        if (_settings?.JournalRetention is { } retention && retention.CutoffDate() is { } cutoff)
            _db.DeleteOlderThan(cutoff);

        // Mark stale Running entries as Cancelled (from previous unclean shutdown)
        _db.CompleteStaleRunning();

        var entries = _db.LoadAll(MaxEntries);
        for (var i = entries.Count - 1; i >= 0; i--)
            Entries.Insert(0, entries[i]);
    }

    public JournalEntry Add(JournalEventKind eventKind, object[]? titleArgs = null, string? repositoryName = null)
    {
        var entry = new JournalEntry(eventKind, titleArgs, repositoryName: repositoryName);
        entry.Id = _db.Insert(entry);
        _db.Trim(MaxEntries);

        void InsertEntry()
        {
            Entries.Insert(0, entry);

            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        }

        if (Dispatcher.UIThread.CheckAccess())
            InsertEntry();
        else
            Dispatcher.UIThread.Post(InsertEntry);

        return entry;
    }

    public void Complete(JournalEntry entry, JournalResult result, string? detail = null)
    {
        entry.Complete(result, detail);
        _db.Update(entry);
        if (!IsActive)
            UnreadCount++;
        _notifications?.NotifyCompleted(entry);
    }

    public void DirectAdd(JournalEntry entry)
    {
        Entries.Insert(0, entry);
    }

    public void MarkAllRead() => UnreadCount = 0;

    public void ClearFinished()
    {
        _db.DeleteFinished();
        var finished = Entries.Where(e => e.IsFinished).ToList();
        foreach (var e in finished)
            Entries.Remove(e);
        UnreadCount = 0;
    }
}
