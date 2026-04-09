using BorgMate.Models;

namespace BorgMate.Tests;

public class JournalEntryTests
{
    [Fact]
    public void Constructor_SetsDefaults()
    {
        var entry = new JournalEntry(JournalEventKind.Backup);
        Assert.Equal(JournalResult.Running, entry.Result);
        Assert.False(entry.IsFinished);
        Assert.Null(entry.CompletedAt);
        Assert.Null(entry.Detail);
    }

    [Fact]
    public void Constructor_SetsProvidedValues()
    {
        var started = new DateTime(2026, 4, 7, 12, 0, 0);
        var entry = new JournalEntry(JournalEventKind.Restore, ["repo1"],
            detail: "some detail", repositoryName: "my-repo", startedAt: started, id: 42);

        Assert.Equal(JournalEventKind.Restore, entry.EventKind);
        Assert.Equal("my-repo", entry.RepositoryName);
        Assert.Equal(started, entry.StartedAt);
        Assert.Equal(42, entry.Id);
        Assert.Equal("some detail", entry.Detail);
    }

    [Fact]
    public void IsFinished_False_WhenRunning()
    {
        var entry = new JournalEntry(JournalEventKind.Backup);
        Assert.False(entry.IsFinished);
    }

    [Theory]
    [InlineData(JournalResult.Completed)]
    [InlineData(JournalResult.Failed)]
    [InlineData(JournalResult.Cancelled)]
    public void IsFinished_True_WhenNotRunning(JournalResult result)
    {
        var entry = new JournalEntry(JournalEventKind.Backup, result: result);
        Assert.True(entry.IsFinished);
    }

    [Fact]
    public void IsCancelled_True_OnlyCancelled()
    {
        var entry = new JournalEntry(JournalEventKind.Backup, result: JournalResult.Cancelled);
        Assert.True(entry.IsCancelled);
        Assert.False(entry.IsFailed);
        Assert.False(entry.IsSuccess);
    }

    [Fact]
    public void IsFailed_True_OnlyFailed()
    {
        var entry = new JournalEntry(JournalEventKind.Backup, result: JournalResult.Failed);
        Assert.True(entry.IsFailed);
        Assert.False(entry.IsCancelled);
        Assert.False(entry.IsSuccess);
    }

    [Fact]
    public void IsSuccess_True_OnlyCompleted()
    {
        var entry = new JournalEntry(JournalEventKind.Backup, result: JournalResult.Completed);
        Assert.True(entry.IsSuccess);
        Assert.False(entry.IsCancelled);
        Assert.False(entry.IsFailed);
    }

    [Fact]
    public void Complete_SetsResultAndCompletedAt()
    {
        var entry = new JournalEntry(JournalEventKind.Check);
        Assert.False(entry.IsFinished);

        entry.Complete(JournalResult.Completed, "all good");

        Assert.True(entry.IsFinished);
        Assert.True(entry.IsSuccess);
        Assert.NotNull(entry.CompletedAt);
        Assert.Equal("all good", entry.Detail);
    }

    [Fact]
    public void Complete_PreservesExistingDetail_WhenNullPassed()
    {
        var entry = new JournalEntry(JournalEventKind.Backup, detail: "original");
        entry.Complete(JournalResult.Completed);
        Assert.Equal("original", entry.Detail);
    }

    [Fact]
    public void Complete_OverridesDetail_WhenProvided()
    {
        var entry = new JournalEntry(JournalEventKind.Backup, detail: "original");
        entry.Complete(JournalResult.Failed, "new detail");
        Assert.Equal("new detail", entry.Detail);
    }

    [Fact]
    public void Complete_FiresPropertyChanged()
    {
        var entry = new JournalEntry(JournalEventKind.Backup);
        var changedProps = new List<string>();
        entry.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        entry.Complete(JournalResult.Completed);

        Assert.Contains(nameof(JournalEntry.Result), changedProps);
        Assert.Contains(nameof(JournalEntry.IsFinished), changedProps);
        Assert.Contains(nameof(JournalEntry.IsSuccess), changedProps);
        Assert.Contains(nameof(JournalEntry.CompletedAtDisplay), changedProps);
    }

    [Theory]
    [InlineData(JournalEventKind.Backup, 0)]
    [InlineData(JournalEventKind.Prune, 1)]
    [InlineData(JournalEventKind.Check, 2)]
    [InlineData(JournalEventKind.Compact, 3)]
    [InlineData(JournalEventKind.Delete, 4)]
    [InlineData(JournalEventKind.Create, 5)]
    [InlineData(JournalEventKind.PassphraseFailed, 6)]
    [InlineData(JournalEventKind.Restore, 7)]
    public void EventKind_HasStableIntegerValues(JournalEventKind kind, int expected)
    {
        Assert.Equal(expected, (int)kind);
    }
}
