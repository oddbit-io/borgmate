using System;
using System.Collections.Concurrent;
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
/// Facade over per-repo job queues. Jobs for different repos run in parallel;
/// jobs for the same repo run sequentially. Public API is unchanged from the
/// single-queue version — consumers inject JobQueueService as before.
/// </summary>
public class JobQueueService : IDisposable
{
    private readonly ConcurrentDictionary<string, RepoJobQueue> _queues = new();
    private readonly ILogger<JobQueueService>? _logger;
    private const string GlobalQueueKey = "__global__";

    /// <summary>Aggregate job collection across all per-repo queues. Mutate only from UI thread.</summary>
    public ObservableCollection<BorgJob> Jobs { get; } = [];

    public int PendingCount => _queues.Values.Sum(q => q.ActiveCount);
    public bool HasRunningJobs => _queues.Values.Any(q => q.HasRunningJobs);
    public bool HasPendingCommand => _queues.Values.Any(q => q.HasPendingCommand);

    public event Action<BorgJob, BorgResult>? JobCompleted;

    public JobQueueService() : this(null) { }

    public JobQueueService(ILogger<JobQueueService>? logger)
    {
        _logger = logger;
    }

    private RepoJobQueue GetOrCreateQueue(string? repoPath)
    {
        var key = repoPath ?? GlobalQueueKey;
        return _queues.GetOrAdd(key, k =>
        {
            var queue = new RepoJobQueue(repoPath, _logger);
            queue.JobCompleted += OnRepoJobCompleted;
            return queue;
        });
    }

    private void OnRepoJobCompleted(BorgJob job, BorgResult result)
    {
        try { JobCompleted?.Invoke(job, result); }
        catch (Exception ex) { _logger?.LogError(ex, "JobCompleted handler threw for job {Name}", job.Name); }

        // Auto-remove completed query jobs to free stdout data
        if (job.Kind == BorgJobKind.Query)
        {
            job.StandardOutput.Clear();
            job.StandardError.Clear();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Jobs.Remove(job));
        }
    }

    public BorgJob Enqueue(string name, Func<BorgJob, CancellationToken, IProgress<string>, Task<BorgResult>> work,
        BorgJobKind kind = BorgJobKind.Command, string? tag = null, string? repoPath = null,
        JournalEntry? journalEntry = null)
    {
        // Deduplicate by tag across all queues
        if (tag is not null)
        {
            var existing = Jobs.FirstOrDefault(j => j.Tag == tag && j.Status is BorgJobStatus.Pending or BorgJobStatus.Running);
            if (existing is not null)
                return existing;
        }

        var job = new BorgJob { Name = name, Work = work, Kind = kind, Tag = tag, RepoPath = repoPath, JournalEntry = journalEntry };

        // Command enqueue cancels queries for the same repo
        if (kind == BorgJobKind.Command && repoPath is not null)
        {
            if (CancelQueryByRepoPath(repoPath))
            {
                var queue = GetOrCreateQueue(repoPath);
                queue.NotifyCancelledQuery();
            }
        }

        Jobs.Add(job);
        GetOrCreateQueue(repoPath).Enqueue(job);
        return job;
    }

    public void CancelPendingByRepoPath(string repoPath)
    {
        foreach (var job in Jobs.Where(j => j.RepoPath == repoPath && j.Status == BorgJobStatus.Pending))
            job.Cts.Cancel();
    }

    public bool CancelQueryByRepoPath(string repoPath)
    {
        var queries = Jobs.Where(j => j.RepoPath == repoPath
            && j.Kind == BorgJobKind.Query
            && j.Status is BorgJobStatus.Pending or BorgJobStatus.Running).ToList();
        foreach (var j in queries)
            j.Cts.Cancel();
        return queries.Count > 0;
    }

    public void ClearQueryInvalidated()
    {
        foreach (var q in _queues.Values)
            q.ClearQueryInvalidated();
    }

    public bool ConsumeQueryInvalidated()
    {
        var any = false;
        foreach (var q in _queues.Values)
            if (q.ConsumeQueryInvalidated()) any = true;
        return any;
    }

    public void Cancel(BorgJob job)
    {
        job.Cts.Cancel();

        if (job.Status == BorgJobStatus.Pending)
        {
            job.Status = BorgJobStatus.Cancelled;
            job.StatusMessage = string.Empty;
            job.CompletedAt = DateTime.Now;
            job.UpdateElapsed();
            job.Completion.TrySetResult(new BorgResult(-1, "", "", WasCancelled: true));
        }
    }

    public void Remove(BorgJob job)
    {
        if (job.Status is BorgJobStatus.Pending)
            Cancel(job);
        if (job.Status is not BorgJobStatus.Running)
            Jobs.Remove(job);
    }

    public void ClearCompleted()
    {
        var done = Jobs.Where(j => j.Status is BorgJobStatus.Completed or BorgJobStatus.Failed or BorgJobStatus.Cancelled).ToList();
        foreach (var j in done)
            Jobs.Remove(j);
    }

    public void UpdateRunningElapsed()
    {
        foreach (var job in Jobs.Where(j => j.Status == BorgJobStatus.Running))
            job.UpdateElapsed();
    }

    public void Dispose()
    {
        // Cancel all pending/running jobs
        foreach (var job in Jobs.Where(j => j.Status is BorgJobStatus.Pending or BorgJobStatus.Running))
        {
            try { job.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        // Dispose all per-repo queues
        foreach (var q in _queues.Values)
            q.Dispose();
        _queues.Clear();
    }
}
