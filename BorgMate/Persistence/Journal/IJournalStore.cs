using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Journal;
using BorgMate.Persistence.Configuration;
using OneOf;

namespace BorgMate.Persistence.Journal;

public interface IJournalStore
{
    int UnreadCount { get; }
    bool IsActive { get; set; }
    ObservableCollection<JournalEntry> Entries { get; }
    event Action<JournalSnapshot>? Changed;
    JournalSnapshot GetSnapshot();

    /// <summary>Loads persisted journal entries into memory synchronously.</summary>
    OneOf<JournalSnapshot, StoreError> Load(CancellationToken ct = default);

    Task<OneOf<JournalSnapshot, StoreError>> LoadAsync(CancellationToken ct = default);
    JournalEntry Add(JournalEventKind eventKind, object[]? titleArgs = null, string? repositoryName = null);
    void Complete(JournalEntry entry, JournalResult result, string? detail = null);
    void DirectAdd(JournalEntry entry);
    void MarkAllRead();
    void ClearFinished();
}
