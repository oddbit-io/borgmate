using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BorgMate.Models;
using BorgMate.Services.Borg;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Queue;

/// <summary>
/// Sequential background job queue. Jobs execute one at a time on a background thread
/// via System.Threading.Channels. Supports cancellation, tag-based deduplication,
/// and query invalidation tracking.
/// </summary>
public class JobQueueService : IDisposable
{
    private readonly Channel<BorgJob> _channel = Channel.CreateUnbounded<BorgJob>();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ILogger<JobQueueService>? _logger;

    // Atomic counters — safe to update from any thread without iterating Jobs
    private int _activeCount;   // pending + running
    private int _runningCount;
    private int _commandActiveCount;

    /// <summary>
    /// Job collection. Only mutate (Add/Remove) from the UI thread.
    /// Job property changes from background threads are fine — Avalonia marshals PropertyChanged.
    /// </summary>
    public ObservableCollection<BorgJob> Jobs { get; } = [];

    /// <summary>Active job count (pending + running). Used for badge display.</summary>
    public int PendingCount => Volatile.Read(ref _activeCount);
    public bool HasRunningJobs => Volatile.Read(ref _runningCount) > 0;

    private int _hadCancelledQuery;
    private int _queryInvalidated;

    /// <summary>True while any command-type job is pending or running.</summary>
    public bool HasPendingCommand => Volatile.Read(ref _commandActiveCount) > 0;

    public JobQueueService() : this(null) { }

    public JobQueueService(ILogger<JobQueueService>? logger)
    {
        _logger = logger;
        _ = Task.Run(async () =>
        {
            try { await ProcessLoopAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogCritical(ex, "Job queue processing loop crashed");
            }
        });
    }

    /// <summary>
    /// Enqueues a job for sequential execution. Called from UI thread.
    /// If tag is set and a job with the same tag is already pending/running, returns the existing job.
    /// Command jobs cancel pending/running query jobs for the same repo.
    /// </summary>
    public BorgJob Enqueue(string name, Func<BorgJob, CancellationToken, IProgress<string>, Task<BorgResult>> work,
        BorgJobKind kind = BorgJobKind.Command, string? tag = null, string? repoPath = null,
        JournalEntry? journalEntry = null)
    {
        // Deduplicate: if a job with the same tag is already pending or running, return it
        if (tag is not null)
        {
            var existing = Jobs.FirstOrDefault(j => j.Tag == tag && j.Status is BorgJobStatus.Pending or BorgJobStatus.Running);
            if (existing is not null)
                return existing;
        }

        var job = new BorgJob { Name = name, Work = work, Kind = kind, Tag = tag, RepoPath = repoPath, JournalEntry = journalEntry };

        if (kind == BorgJobKind.Command && repoPath is not null)
        {
            if (CancelQueryByRepoPath(repoPath))
                Interlocked.Exchange(ref _hadCancelledQuery, 1);
        }

        Jobs.Add(job);
        Interlocked.Increment(ref _activeCount);
        if (kind == BorgJobKind.Command)
            Interlocked.Increment(ref _commandActiveCount);
        CheckQueryInvalidated();
        _channel.Writer.TryWrite(job);
        return job;
    }

    /// <summary>
    /// Cancel all pending jobs targeting the given repo. Safe from any thread.
    /// </summary>
    public void CancelPendingByRepoPath(string repoPath)
    {
        foreach (var job in Jobs.Where(j => j.RepoPath == repoPath && j.Status == BorgJobStatus.Pending))
            job.Cts.Cancel();
    }

    /// <summary>
    /// Clear the query-invalidated flag so that the next command job completion
    /// does not trigger a re-query. Use before enqueuing read-only command jobs (e.g. restore).
    /// </summary>
    public void ClearQueryInvalidated()
    {
        Interlocked.Exchange(ref _hadCancelledQuery, 0);
        Interlocked.Exchange(ref _queryInvalidated, 0);
    }

    /// <summary>
    /// Cancel all query jobs (pending or running) targeting the given repo.
    /// </summary>
    /// <returns>True if any jobs were cancelled.</returns>
    public bool CancelQueryByRepoPath(string repoPath)
    {
        var queries = Jobs.Where(j => j.RepoPath == repoPath
            && j.Kind == BorgJobKind.Query
            && j.Status is BorgJobStatus.Pending or BorgJobStatus.Running).ToList();
        foreach (var j in queries)
            j.Cts.Cancel();
        return queries.Count > 0;
    }

    /// <summary>
    /// Called from UI thread.
    /// </summary>
    public void Cancel(BorgJob job)
    {
        // Always cancel the token regardless of status — the status field may be stale
        // on ARM64 since it's set from the background thread without a memory barrier.
        job.Cts.Cancel();

        if (job.Status == BorgJobStatus.Pending)
        {
            job.Status = BorgJobStatus.Cancelled;
            job.StatusMessage = string.Empty;
            job.CompletedAt = DateTime.Now;
            job.UpdateElapsed();
            job.Completion.TrySetResult(new BorgResult(-1, "", "", WasCancelled: true));
            FinishJob(job);
        }
    }

    /// <summary>
    /// Called from UI thread.
    /// </summary>
    public void Remove(BorgJob job)
    {
        if (job.Status is BorgJobStatus.Pending)
            Cancel(job);

        if (job.Status is not BorgJobStatus.Running)
            Jobs.Remove(job);
    }

    /// <summary>
    /// Called from UI thread.
    /// </summary>
    public void ClearCompleted()
    {
        var done = Jobs.Where(j => j.Status is BorgJobStatus.Completed or BorgJobStatus.Failed or BorgJobStatus.Cancelled).ToList();
        foreach (var j in done)
            Jobs.Remove(j);
    }

    /// <summary>
    /// Update elapsed time display for all running jobs. Called by UI timer.
    /// </summary>
    public void UpdateRunningElapsed()
    {
        foreach (var job in Jobs.Where(j => j.Status == BorgJobStatus.Running))
            job.UpdateElapsed();
    }

    private async Task ProcessLoopAsync()
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(_shutdownCts.Token))
        {
            if (job.Cts.IsCancellationRequested)
            {
                job.Status = BorgJobStatus.Cancelled;
                job.StatusMessage = string.Empty;
                job.CompletedAt = DateTime.Now;
                job.UpdateElapsed();
                FinishJob(job);
                job.Completion.TrySetResult(new BorgResult(-1, "", "", WasCancelled: true));
                continue;
            }

            job.Status = BorgJobStatus.Running;
            job.StartedAt = DateTime.Now;
            job.StatusMessage = string.Empty;
            Interlocked.Increment(ref _runningCount);
            CheckQueryInvalidated();

            BorgResult result;
            try
            {
                BorgJob.Current.Value = job;
                var progress = new Progress<string>(msg => job.StatusMessage = msg);
                result = await job.Work!(job, job.Cts.Token, progress);
            }
            catch (OperationCanceledException)
            {
                result = new BorgResult(-1, "", "", WasCancelled: true);
            }
            finally
            {
                BorgJob.Current.Value = null;
            }

            var finalStatus = job.Cts.IsCancellationRequested
                ? BorgJobStatus.Cancelled
                : result.Success ? BorgJobStatus.Completed : BorgJobStatus.Failed;

            var finalMessage = job.Cts.IsCancellationRequested
                ? string.Empty
                : result.Success
                    ? job.StatusMessage
                    : result.ErrorMessage;

            job.Status = finalStatus;
            job.Progress = null;
            job.StatusMessage = finalMessage;
            job.CompletedAt = DateTime.Now;
            job.UpdateElapsed();
            Interlocked.Decrement(ref _runningCount);
            FinishJob(job);

            job.Completion.TrySetResult(result);

            // Auto-remove completed query jobs to free large stdout data
            // held by Completion.Task.Result. Command jobs stay for debug page.
            if (job.Kind == BorgJobKind.Query)
            {
                job.StandardOutput.Clear();
                job.StandardError.Clear();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Jobs.Remove(job));
            }
        }
    }

    /// <summary>
    /// Decrement active counters and publish. Safe from any thread.
    /// </summary>
    private void FinishJob(BorgJob job)
    {
        Interlocked.Decrement(ref _activeCount);
        if (job.Kind == BorgJobKind.Command)
            Interlocked.Decrement(ref _commandActiveCount);
        CheckQueryInvalidated();
    }

    /// <summary>
    /// Returns true once when command jobs cancelled query jobs, signaling that
    /// cached query results are stale. Polled by MainWindowViewModel's timer.
    /// </summary>
    public bool ConsumeQueryInvalidated() =>
        Interlocked.Exchange(ref _queryInvalidated, 0) != 0;

    private void CheckQueryInvalidated()
    {
        if (Volatile.Read(ref _hadCancelledQuery) != 0 && !HasPendingCommand)
        {
            Interlocked.Exchange(ref _hadCancelledQuery, 0);
            Interlocked.Exchange(ref _queryInvalidated, 1);
        }
    }

    public void Dispose()
    {
        foreach (var job in Jobs.Where(j => j.Status is BorgJobStatus.Pending or BorgJobStatus.Running))
        {
            try { job.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        try { _shutdownCts.Cancel(); } catch (ObjectDisposedException) { }
        _shutdownCts.Dispose();
    }
}
