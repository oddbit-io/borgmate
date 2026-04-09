using BorgMate.Models;
using BorgMate.ViewModels;

namespace BorgMate.Tests;

public class JobProgressTrackerTests
{
    [Fact]
    public void HasActiveJob_False_Initially()
    {
        var tracker = new JobProgressTracker();
        Assert.False(tracker.HasActiveJob);
    }

    [Fact]
    public void SetActiveJob_SetsHasActiveJob()
    {
        var tracker = new JobProgressTracker();
        var job = new BorgJob { Name = "test" };

        tracker.SetActiveJob(job);

        Assert.True(tracker.HasActiveJob);
    }

    [Fact]
    public void SetActiveJob_Null_ClearsAll()
    {
        var tracker = new JobProgressTracker();
        var job = new BorgJob { Name = "test" };

        tracker.SetActiveJob(job);
        tracker.SetActiveJob(null);

        Assert.False(tracker.HasActiveJob);
        Assert.Null(tracker.ActiveStatusMessage);
        Assert.Null(tracker.ActiveElapsed);
        Assert.Null(tracker.ActiveProgress);
        Assert.Null(tracker.ActiveProgressDisplay);
    }

    [Fact]
    public void SetActiveJob_SyncsStatus()
    {
        var tracker = new JobProgressTracker();
        var job = new BorgJob { Name = "test", StatusMessage = "Running backup" };

        tracker.SetActiveJob(job);

        Assert.Equal("Running backup", tracker.ActiveStatusMessage);
    }

    [Fact]
    public void SetActiveJob_SameJob_NoOp()
    {
        var tracker = new JobProgressTracker();
        var job = new BorgJob { Name = "test" };

        tracker.SetActiveJob(job);
        tracker.SetActiveJob(job); // same job again

        Assert.True(tracker.HasActiveJob);
    }

    [Fact]
    public void SetActiveJob_DifferentJob_Switches()
    {
        var tracker = new JobProgressTracker();
        var job1 = new BorgJob { Name = "job1", StatusMessage = "first" };
        var job2 = new BorgJob { Name = "job2", StatusMessage = "second" };

        tracker.SetActiveJob(job1);
        Assert.Equal("first", tracker.ActiveStatusMessage);

        tracker.SetActiveJob(job2);
        Assert.Equal("second", tracker.ActiveStatusMessage);
    }

    [Fact]
    public void ActiveProgressDisplay_NoProgress_ShowsZero()
    {
        var tracker = new JobProgressTracker();
        var job = new BorgJob { Name = "test" };

        tracker.SetActiveJob(job);

        Assert.Equal("0.0%", tracker.ActiveProgressDisplay);
    }
}
