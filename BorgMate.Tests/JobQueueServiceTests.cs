using System.Threading.Tasks;
using BorgMate.Models;
using BorgMate.Services.Borg;
using BorgMate.Services.Queue;

namespace BorgMate.Tests;

public class JobQueueServiceTests : IDisposable
{
    private readonly JobQueueService _svc = new();

    public void Dispose() => _svc.Dispose();

    private static Func<BorgJob, CancellationToken, IProgress<string>, Task<BorgResult>> SuccessWork =>
        (_, _, _) => Task.FromResult(new BorgResult(0, "ok", ""));

    private static Func<BorgJob, CancellationToken, IProgress<string>, Task<BorgResult>> DelayWork(int ms = 5000) =>
        async (_, ct, _) => { await Task.Delay(ms, ct); return new BorgResult(0, "ok", ""); };

    private static Func<BorgJob, CancellationToken, IProgress<string>, Task<BorgResult>> FailWork =>
        (_, _, _) => Task.FromResult(new BorgResult(1, "", "error"));

    // --- Enqueue ---

    [Fact]
    public void Enqueue_AddsJobToCollection()
    {
        var job = _svc.Enqueue("test", SuccessWork);
        Assert.Contains(job, _svc.Jobs);
        Assert.Equal("test", job.Name);
    }

    [Fact]
    public void Enqueue_IncrementsPendingCount()
    {
        _svc.Enqueue("test", DelayWork(), BorgJobKind.Command);
        Assert.True(_svc.PendingCount >= 1);
    }

    [Fact]
    public void Enqueue_ModifyingJob_SetsHasPendingCommand()
    {
        _svc.Enqueue("test", DelayWork(), BorgJobKind.Command);
        Assert.True(_svc.HasPendingCommand);
    }

    [Fact]
    public void Enqueue_FetchingJob_DoesNotSetHasPendingCommand()
    {
        _svc.Enqueue("test", DelayWork(), BorgJobKind.Query);
        Assert.False(_svc.HasPendingCommand);
    }

    // --- Tag deduplication ---

    [Fact]
    public void Enqueue_SameTag_ReturnsSameJob()
    {
        var job1 = _svc.Enqueue("a", DelayWork(), tag: "unique");
        var job2 = _svc.Enqueue("b", DelayWork(), tag: "unique");
        Assert.Same(job1, job2);
    }

    [Fact]
    public void Enqueue_DifferentTags_ReturnsDifferentJobs()
    {
        var job1 = _svc.Enqueue("a", DelayWork(), tag: "tag1");
        var job2 = _svc.Enqueue("b", DelayWork(), tag: "tag2");
        Assert.NotSame(job1, job2);
    }

    [Fact]
    public void Enqueue_NullTag_NeverDeduplicates()
    {
        var job1 = _svc.Enqueue("a", DelayWork());
        var job2 = _svc.Enqueue("b", DelayWork());
        Assert.NotSame(job1, job2);
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_PendingJob_SetsCancelled()
    {
        var job = _svc.Enqueue("test", DelayWork());
        _svc.Cancel(job);

        Assert.Equal(BorgJobStatus.Cancelled, job.Status);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task Cancel_PendingJob_CompletionReturns()
    {
        var job = _svc.Enqueue("test", DelayWork());
        _svc.Cancel(job);

        var result = await job.Completion.Task;
        Assert.True(result.WasCancelled);
    }

    // --- CancelPendingByRepoPath ---

    [Fact]
    public void CancelPendingByRepoPath_CancelsMatchingJobs()
    {
        // Blocker keeps subsequent jobs in Pending state
        _svc.Enqueue("blocker", DelayWork(5000));
        var job1 = _svc.Enqueue("a", DelayWork(), repoPath: "/repo1");
        var job2 = _svc.Enqueue("b", DelayWork(), repoPath: "/repo2");

        _svc.CancelPendingByRepoPath("/repo1");

        Assert.True(job1.Cts.IsCancellationRequested);
        Assert.False(job2.Cts.IsCancellationRequested);
    }

    // --- CancelQueryByRepoPath ---

    [Fact]
    public void CancelQueryByRepoPath_CancelsFetchingOnly()
    {
        var fetching = _svc.Enqueue("f", DelayWork(), BorgJobKind.Query, repoPath: "/repo");
        var modifying = _svc.Enqueue("m", DelayWork(), BorgJobKind.Command, repoPath: "/repo");

        _svc.CancelQueryByRepoPath("/repo");

        Assert.True(fetching.Cts.IsCancellationRequested);
        Assert.False(modifying.Cts.IsCancellationRequested);
    }

    [Fact]
    public void CancelQueryByRepoPath_ReturnsTrueWhenCancelled()
    {
        _svc.Enqueue("f", DelayWork(), BorgJobKind.Query, repoPath: "/repo");
        Assert.True(_svc.CancelQueryByRepoPath("/repo"));
    }

    [Fact]
    public void CancelQueryByRepoPath_ReturnsFalseWhenNone()
    {
        Assert.False(_svc.CancelQueryByRepoPath("/repo"));
    }

    // --- Remove ---

    [Fact]
    public void Remove_PendingJob_CancelsAndRemoves()
    {
        // Enqueue a slow blocker first so "victim" stays pending
        _svc.Enqueue("blocker", DelayWork(5000));
        var victim = _svc.Enqueue("victim", DelayWork());

        _svc.Remove(victim);

        Assert.DoesNotContain(victim, _svc.Jobs);
        Assert.Equal(BorgJobStatus.Cancelled, victim.Status);
    }

    // --- ClearCompleted ---

    [Fact]
    public void ClearCompleted_RemovesDoneJobs()
    {
        var job = _svc.Enqueue("test", DelayWork());
        _svc.Cancel(job); // makes it Cancelled
        Assert.Contains(job, _svc.Jobs);

        _svc.ClearCompleted();
        Assert.DoesNotContain(job, _svc.Jobs);
    }

    // --- ClearQueryInvalidated ---

    [Fact]
    public void ClearQueryInvalidated_ResetFlag()
    {
        _svc.ClearQueryInvalidated();
        Assert.False(_svc.ConsumeQueryInvalidated());
    }

    // --- ConsumeQueryInvalidated ---

    [Fact]
    public void ConsumeQueryInvalidated_FalseInitially()
    {
        Assert.False(_svc.ConsumeQueryInvalidated());
    }

    // --- ProcessLoop integration ---

    [Fact]
    public async Task ProcessLoop_SuccessfulJob_Completes()
    {
        var job = _svc.Enqueue("test", SuccessWork);
        var result = await job.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Success);
        Assert.Equal("ok", result.StandardOutput);
    }

    [Fact]
    public async Task ProcessLoop_FailedJob_ReportsError()
    {
        var job = _svc.Enqueue("test", FailWork);
        var result = await job.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Success);
        Assert.Equal("error", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessLoop_CancelledBeforeRun_ReturnsCancelled()
    {
        // Enqueue two jobs — first is slow, second gets cancelled while pending
        _svc.Enqueue("blocker", DelayWork(2000));
        var job = _svc.Enqueue("victim", DelayWork());
        job.Cts.Cancel();

        var result = await job.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.WasCancelled);
    }

    [Fact]
    public async Task ProcessLoop_JobsRunSequentially()
    {
        var order = new List<int>();
        var job1 = _svc.Enqueue("first", async (_, _, _) =>
        {
            order.Add(1);
            await Task.Delay(50);
            return new BorgResult(0, "", "");
        });
        var job2 = _svc.Enqueue("second", (_, _, _) =>
        {
            order.Add(2);
            return Task.FromResult(new BorgResult(0, "", ""));
        });

        await job2.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], order);
    }

    // --- Modifying job cancels fetching ---

    [Fact]
    public void Enqueue_ModifyingJob_CancelsFetchingForSameRepo()
    {
        var fetching = _svc.Enqueue("fetch", DelayWork(), BorgJobKind.Query, repoPath: "/repo");
        _svc.Enqueue("modify", DelayWork(), BorgJobKind.Command, repoPath: "/repo");

        Assert.True(fetching.Cts.IsCancellationRequested);
    }

    [Fact]
    public void Enqueue_ModifyingJob_DoesNotCancelFetchingForDifferentRepo()
    {
        var fetching = _svc.Enqueue("fetch", DelayWork(), BorgJobKind.Query, repoPath: "/repo1");
        _svc.Enqueue("modify", DelayWork(), BorgJobKind.Command, repoPath: "/repo2");

        Assert.False(fetching.Cts.IsCancellationRequested);
    }
}
