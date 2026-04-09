using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using BorgMate.Models;
using BorgMate.Services.Journal;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Services.Mocks;

/// <summary>
/// Journal service for demo mode — no SQLite, entries only in memory.
/// </summary>
public partial class MockJournalService : ObservableObject, IJournalService
{
    [ObservableProperty]
    private int _unreadCount;

    public bool IsActive { get; set; }
    public ObservableCollection<JournalEntry> Entries { get; } = [];

    public void Load() { }

    public JournalEntry Add(JournalEventKind eventKind, object[]? titleArgs = null, string? repositoryName = null)
    {
        var entry = new JournalEntry(eventKind, titleArgs, repositoryName: repositoryName);
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Insert(0, entry);
            if (!IsActive)
                UnreadCount++;
        });
        return entry;
    }

    public void Complete(JournalEntry entry, JournalResult result, string? detail = null)
    {
        entry.Complete(result, detail);
    }

    public void DirectAdd(JournalEntry entry)
    {
        Entries.Insert(0, entry);
    }

    public void MarkAllRead() => UnreadCount = 0;

    public void ClearFinished()
    {
        var finished = Entries.Where(e => e.IsFinished).ToList();
        foreach (var e in finished)
            Entries.Remove(e);
        UnreadCount = 0;
    }
}
