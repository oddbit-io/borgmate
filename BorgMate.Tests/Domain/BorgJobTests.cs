using BorgMate.Infrastructure.Borg;
using BorgMate.Runtime.Jobs;
using BorgJob = BorgMate.Runtime.Jobs.BorgJob;

namespace BorgMate.Tests.Domain;

public class BorgJobTests
{
    [Fact]
    public void Constructor_SetsDefaults()
    {
        var job = new BorgJob();
        Assert.Equal(BorgJobStatus.Pending, job.Status);
        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal(string.Empty, job.Name);
        Assert.Null(job.RepoPath);
        Assert.Null(job.Tag);
    }

    [Fact]
    public void Kind_DefaultsFetching()
    {
        var job = new BorgJob();
        Assert.Equal(BorgJobKind.Query, job.Kind);
    }

    [Fact]
    public void Kind_CanBeSetModifying()
    {
        var job = new BorgJob { Kind = BorgJobKind.Command };
        Assert.Equal(BorgJobKind.Command, job.Kind);
    }

    [Fact]
    public void Cts_IsNotCancelled_Initially()
    {
        var job = new BorgJob();
        Assert.False(job.Cts.IsCancellationRequested);
    }

    [Fact]
    public void Cts_CanBeCancelled()
    {
        var job = new BorgJob();
        job.Cts.Cancel();
        Assert.True(job.Cts.IsCancellationRequested);
    }

    [Fact]
    public async Task Completion_CanBeCompleted()
    {
        var job = new BorgJob();
        var result = new BorgResult(0, "ok", "");
        job.Completion.SetResult(result);

        Assert.True(job.Completion.Task.IsCompleted);
        Assert.Same(result, await job.Completion.Task);
    }

    [Fact]
    public void Tag_CanBeSet()
    {
        var job = new BorgJob { Tag = "list:repo1" };
        Assert.Equal("list:repo1", job.Tag);
    }

    [Fact]
    public void RepoPath_CanBeSet()
    {
        var job = new BorgJob { RepoPath = "/data/repo" };
        Assert.Equal("/data/repo", job.RepoPath);
    }

    [Fact]
    public void StatusMessage_FiresPropertyChanged()
    {
        var job = new BorgJob();
        var changed = new List<string>();
        job.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        job.StatusMessage = "Processing...";

        Assert.Contains(nameof(BorgJob.StatusMessage), changed);
        Assert.Equal("Processing...", job.StatusMessage);
    }

    [Fact]
    public void Progress_FiresPropertyChanged()
    {
        var job = new BorgJob();
        var changed = new List<string>();
        job.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        job.Progress = 42.5;

        Assert.Contains(nameof(BorgJob.Progress), changed);
        Assert.Equal(42.5, job.Progress);
    }

    [Fact]
    public void Progress_CanBeNull()
    {
        var job = new BorgJob { Progress = 50.0 };
        job.Progress = null;
        Assert.Null(job.Progress);
    }

    [Fact]
    public void EstimateSpeed_QuantizedProgressAfterFastPhase_ReportsActualThroughput()
    {
        const long totalSize = 2_000_000_000_000;
        var clock = new ManualTimeProvider();
        var job = new BorgJob { TotalSize = totalSize, Clock = clock };

        // Unchanged files fly by: 100 GB in one second.
        job.RecordProgress(40.0);
        clock.Advance(TimeSpan.FromSeconds(1));
        job.RecordProgress(45.0);

        // New data uploads at ~5 MB/s while borg keeps printing the same rounded size for 35 minutes.
        var plateau = TimeSpan.FromMinutes(35);
        var step = TimeSpan.FromMilliseconds(500);
        for (var elapsed = TimeSpan.Zero; elapsed < plateau; elapsed += step)
        {
            clock.Advance(step);
            job.RecordProgress(45.0);
        }

        clock.Advance(step);
        job.RecordProgress(45.5);
        job.Progress = 45.5;

        var expected = totalSize * 0.005 / (plateau + step).TotalSeconds;
        var speed = job.EstimateSpeed();
        Assert.NotNull(speed);
        Assert.InRange(speed.Value, expected * 0.99, expected * 1.01);
    }
}
