using System.Collections.ObjectModel;
using BorgMate.Models;

namespace BorgMate.Services.Journal;

public interface IJournalService
{
    int UnreadCount { get; }
    bool IsActive { get; set; }
    ObservableCollection<JournalEntry> Entries { get; }
    void Load();
    JournalEntry Add(JournalEventKind eventKind, object[]? titleArgs = null, string? repositoryName = null);
    void Complete(JournalEntry entry, JournalResult result, string? detail = null);
    void DirectAdd(JournalEntry entry);
    void MarkAllRead();
    void ClearFinished();
}
