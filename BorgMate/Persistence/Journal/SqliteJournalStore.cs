using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Persistence.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using OneOf;

namespace BorgMate.Persistence.Journal;

public partial class SqliteJournalStore : ObservableObject, IJournalStore
{
    private const int MaxEntries = 200;
    private readonly JournalDatabase _db = new();
    private readonly NotificationService? _notifications;
    private readonly AppSettings? _settings;

    [ObservableProperty]
    private int _unreadCount;

    public SqliteJournalStore()
    {
    }

    public SqliteJournalStore(NotificationService notifications, AppSettings settings)
    {
        _notifications = notifications;
        _settings = settings;
    }

    public bool IsActive { get; set; }

    public ObservableCollection<JournalEntry> Entries { get; } = [];

    public event Action<JournalSnapshot>? Changed;

    public JournalSnapshot GetSnapshot()
    {
        return new JournalSnapshot(Entries.ToList(), UnreadCount);
    }

    /// <inheritdoc />
    public OneOf<JournalSnapshot, StoreError> Load(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            LoadEntries();
            var snapshot = GetSnapshot();
            Changed?.Invoke(snapshot);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StoreError.Io(ex);
        }
    }

    public Task<OneOf<JournalSnapshot, StoreError>> LoadAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Load(ct));
    }

    public JournalEntry Add(JournalEventKind eventKind, object[]? titleArgs = null, string? repositoryName = null)
    {
        var entry = new JournalEntry(eventKind, titleArgs, repositoryName: repositoryName);
        entry.Id = _db.Insert(entry);
        _db.Trim();

        void InsertEntry()
        {
            Entries.Insert(0, entry);

            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
            Changed?.Invoke(GetSnapshot());
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
        Changed?.Invoke(GetSnapshot());
        _notifications?.NotifyCompleted(entry);
    }

    public void DirectAdd(JournalEntry entry)
    {
        Entries.Insert(0, entry);
        Changed?.Invoke(GetSnapshot());
    }

    public void MarkAllRead()
    {
        UnreadCount = 0;
        Changed?.Invoke(GetSnapshot());
    }

    public void ClearFinished()
    {
        _db.DeleteFinished();
        var finished = Entries.Where(e => e.IsFinished).ToList();
        foreach (var e in finished)
            Entries.Remove(e);
        UnreadCount = 0;
        Changed?.Invoke(GetSnapshot());
    }

    private void LoadEntries()
    {
        if (_settings?.JournalRetention is { } retention && retention.CutoffDate() is { } cutoff)
            _db.DeleteOlderThan(cutoff);

        // Mark stale Running entries as Cancelled (from previous unclean shutdown)
        _db.CompleteStaleRunning();

        var entries = _db.LoadAll();
        for (var i = entries.Count - 1; i >= 0; i--)
            Entries.Insert(0, entries[i]);
    }
}
