using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Domain.Journal;
using BorgMate.Persistence.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using OneOf;

namespace BorgMate.Persistence.Journal;

/// <summary>
///     Journal service for demo mode — no SQLite, entries only in memory.
/// </summary>
public partial class MockJournalStore : ObservableObject, IJournalStore
{
    [ObservableProperty]
    private int _unreadCount;

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
        ct.ThrowIfCancellationRequested();
        return GetSnapshot();
    }

    public Task<OneOf<JournalSnapshot, StoreError>> LoadAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Load(ct));
    }

    public JournalEntry Add(JournalEventKind eventKind, object[]? titleArgs = null, string? repositoryName = null)
    {
        var entry = new JournalEntry(eventKind, titleArgs, repositoryName: repositoryName);
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Insert(0, entry);
            if (!IsActive)
                UnreadCount++;
            Changed?.Invoke(GetSnapshot());
        });
        return entry;
    }

    public void Complete(JournalEntry entry, JournalResult result, string? detail = null)
    {
        entry.Complete(result, detail);
        Changed?.Invoke(GetSnapshot());
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
        var finished = Entries.Where(e => e.IsFinished).ToList();
        foreach (var e in finished)
            Entries.Remove(e);
        UnreadCount = 0;
        Changed?.Invoke(GetSnapshot());
    }
}
