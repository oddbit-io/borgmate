using System;
using System.Collections.ObjectModel;
using BorgMate.Models;
using BorgMate.Services.Journal;
using BorgMate.ViewModels;
using NSubstitute;

namespace BorgMate.Tests;

public class NotificationsViewModelTests
{
    private static (NotificationsViewModel vm, ObservableCollection<JournalEntry> entries) CreateVm()
    {
        var journal = Substitute.For<IJournalService>();
        var entries = new ObservableCollection<JournalEntry>();
        journal.Entries.Returns(entries);
        var vm = new NotificationsViewModel(journal);
        return (vm, entries);
    }

    [Fact]
    public void EmptyJournal_NoItems()
    {
        var (vm, _) = CreateVm();
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void FinishedEntry_AppearsInItems()
    {
        var (vm, entries) = CreateVm();

        var entry = new JournalEntry(JournalEventKind.Backup, ["test"], null, "repo",
            DateTime.Now.AddMinutes(-5), DateTime.Now, JournalResult.Completed);
        entries.Add(entry);

        Assert.Single(vm.Items);
        Assert.Equal(entry, vm.Items[0].Entry);
    }

    [Fact]
    public void RunningEntry_NotInItems_UntilCompleted()
    {
        var (vm, entries) = CreateVm();

        var entry = new JournalEntry(JournalEventKind.Backup, ["test"], null, "repo",
            DateTime.Now, null, JournalResult.Running);
        entries.Add(entry);

        Assert.Empty(vm.Items);

        // Complete the entry
        entry.Complete(JournalResult.Completed);

        Assert.Single(vm.Items);
    }

    [Fact]
    public void FailedEntry_AppearsInItems()
    {
        var (vm, entries) = CreateVm();

        var entry = new JournalEntry(JournalEventKind.Check, ["repo"], null, "repo",
            DateTime.Now.AddMinutes(-1), DateTime.Now, JournalResult.Failed);
        entries.Add(entry);

        Assert.Single(vm.Items);
    }

    [Fact]
    public void CancelledEntry_AppearsInItems()
    {
        var (vm, entries) = CreateVm();

        var entry = new JournalEntry(JournalEventKind.Compact, ["repo"], null, "repo",
            DateTime.Now.AddMinutes(-1), DateTime.Now, JournalResult.Cancelled);
        entries.Add(entry);

        Assert.Single(vm.Items);
    }

    [Fact]
    public void MultipleEntries_AllAppear()
    {
        var (vm, entries) = CreateVm();

        entries.Add(new JournalEntry(JournalEventKind.Backup, ["a"], null, "a",
            DateTime.Now.AddMinutes(-10), DateTime.Now.AddMinutes(-5), JournalResult.Completed));
        entries.Add(new JournalEntry(JournalEventKind.Check, ["b"], null, "b",
            DateTime.Now.AddMinutes(-3), DateTime.Now, JournalResult.Failed));

        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void EntriesCleared_ItemsCleared()
    {
        var (vm, entries) = CreateVm();

        entries.Add(new JournalEntry(JournalEventKind.Backup, ["test"], null, "repo",
            DateTime.Now.AddMinutes(-5), DateTime.Now, JournalResult.Completed));
        Assert.Single(vm.Items);

        entries.Clear();
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void EntryRemoved_ItemRemoved()
    {
        var (vm, entries) = CreateVm();

        var entry = new JournalEntry(JournalEventKind.Backup, ["test"], null, "repo",
            DateTime.Now.AddMinutes(-5), DateTime.Now, JournalResult.Completed);
        entries.Add(entry);
        Assert.Single(vm.Items);

        entries.Remove(entry);
        Assert.Empty(vm.Items);
    }
}
