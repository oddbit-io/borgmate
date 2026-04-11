using BorgMate.Models;

namespace BorgMate.Tests;

public class NotificationItemTests
{
    [Fact]
    public void Constructor_WrapsEntry()
    {
        var entry = new JournalEntry(JournalEventKind.Backup, ["repo"], repositoryName: "my-repo");
        var item = new NotificationItem(entry);

        Assert.Same(entry, item.Entry);
        Assert.Equal("my-repo", item.RepositoryName);
    }

    [Fact]
    public void StatusProperties_DelegateToEntry()
    {
        var entry = new JournalEntry(JournalEventKind.Backup, result: JournalResult.Completed);
        var item = new NotificationItem(entry);

        Assert.True(item.IsSuccess);
        Assert.False(item.IsFailed);
        Assert.False(item.IsCancelled);
    }

    [Fact]
    public void EntryComplete_FiresItemPropertyChanged()
    {
        var entry = new JournalEntry(JournalEventKind.Backup);
        var item = new NotificationItem(entry);
        var changed = new List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        entry.Complete(JournalResult.Failed, "disk full");

        Assert.Contains(nameof(NotificationItem.IsFailed), changed);
        Assert.Contains(nameof(NotificationItem.IsSuccess), changed);
        Assert.Contains(nameof(NotificationItem.IsCancelled), changed);
        Assert.Contains(nameof(NotificationItem.DisplayResult), changed);
    }

    [Fact]
    public void EntryDetailChange_FiresItemDetailChanged()
    {
        var entry = new JournalEntry(JournalEventKind.Check);
        var item = new NotificationItem(entry);
        var changed = new List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        entry.Detail = "some error detail";

        Assert.Contains(nameof(NotificationItem.Detail), changed);
        Assert.Equal("some error detail", item.Detail);
    }

    [Fact]
    public void Detach_StopsForwardingEvents()
    {
        var entry = new JournalEntry(JournalEventKind.Backup);
        var item = new NotificationItem(entry);
        var changed = new List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        item.Detach();
        entry.Complete(JournalResult.Completed);

        Assert.Empty(changed);
    }

    [Fact]
    public void Refresh_FiresDisplayProperties()
    {
        var entry = new JournalEntry(JournalEventKind.Backup, result: JournalResult.Completed);
        var item = new NotificationItem(entry);
        var changed = new List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        item.Refresh();

        Assert.Contains(nameof(NotificationItem.DisplayTime), changed);
        Assert.Contains(nameof(NotificationItem.Entry), changed);
        Assert.Contains(nameof(NotificationItem.DisplayResult), changed);
    }
}
