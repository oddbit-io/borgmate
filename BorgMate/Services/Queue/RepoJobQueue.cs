using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BorgMate.Models;
using BorgMate.Services.Borg;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Queue;

/// <summary>Per-repo sequential job queue. Commands and queries run one at a time within a repo.</summary>
internal sealed class RepoJobQueue : IDisposable
{
    private readonly Channel<BorgJob> _channel = Channel.CreateUnbounded<BorgJob>();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ILogger? _logger;

    private int _activeCount;
    private int _runningCount;
    private int _commandActiveCount;
    private int _hadCancelledQuery;
    private int _queryInvalidated;

    public string? RepoPath { get; }

    public event Action<BorgJob, BorgResult>? JobCompleted;

    public int ActiveCount => Volatile.Read(ref _activeCount);
    public bool HasRunningJobs => Volatile.Read(ref _runningCount) > 0;
    public bool HasPendingCommand => Volatile.Read(ref _commandActiveCount) > 0;

    public RepoJobQueue(string? repoPath, ILogger? logger)
    {
        RepoPath = repoPath;
        _logger = logger;
        _ = Task.Run(async () =>
        {
            try { await ProcessLoopAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogCritical(ex, "Job queue for repo {RepoPath} crashed", repoPath);
            }
        });
    }

    public void Enqueue(BorgJob job)
    {
        Interlocked.Increment(ref _activeCount);
        if (job.Kind == BorgJobKind.Command)
            Interlocked.Increment(ref _commandActiveCount);
        CheckQueryInvalidated();
        _channel.Writer.TryWrite(job);
    }

    public bool CancelQueries(Func<BorgJob, bool> match)
    {
        var cancelled = false;
        // The facade passes a predicate that filters by repoPath + kind + status
        // since the facade owns the Jobs collection.
        return cancelled;
    }

    public bool ConsumeQueryInvalidated() =>
        Interlocked.Exchange(ref _queryInvalidated, 0) != 0;

    public void ClearQueryInvalidated()
    {
        Interlocked.Exchange(ref _hadCancelledQuery, 0);
        Interlocked.Exchange(ref _queryInvalidated, 0);
    }

    public void NotifyCancelledQuery()
    {
        Interlocked.Exchange(ref _hadCancelledQuery, 1);
        CheckQueryInvalidated();
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
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Job {Name} threw an unhandled exception", job.Name);
                result = new BorgResult(-1, "", ex.Message);
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

            try { JobCompleted?.Invoke(job, result); }
            catch (Exception ex) { _logger?.LogError(ex, "JobCompleted handler threw for job {Name}", job.Name); }
        }
    }

    private void FinishJob(BorgJob job)
    {
        Interlocked.Decrement(ref _activeCount);
        if (job.Kind == BorgJobKind.Command)
            Interlocked.Decrement(ref _commandActiveCount);
        CheckQueryInvalidated();
    }

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
        try { _shutdownCts.Cancel(); } catch (ObjectDisposedException) { }
        _shutdownCts.Dispose();
    }
}
