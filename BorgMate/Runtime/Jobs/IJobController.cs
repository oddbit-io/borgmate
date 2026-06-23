using System;

namespace BorgMate.Runtime.Jobs;

/// <summary>Presentation-facing control surface for queued borg jobs.</summary>
public interface IJobController
{
    /// <summary>Fires when a real queued job completes.</summary>
    event Action<JobCompleted>? JobCompleted;

    /// <summary>Returns a snapshot of aggregate queue state.</summary>
    JobSnapshot GetSnapshot();

    /// <summary>Cancels a pending or running job.</summary>
    void Cancel(BorgJob job);

    /// <summary>Cancels pending or running query jobs for a repository.</summary>
    void CancelQueries(string repositoryPath);

    /// <summary>Clears completed jobs from the visible job list.</summary>
    void ClearCompleted();

    /// <summary>Refreshes elapsed time for running jobs.</summary>
    void UpdateRunningElapsed();

    /// <summary>Consumes and clears the query-invalidated flag.</summary>
    bool ConsumeQueryInvalidated();

    /// <summary>Clears the query-invalidated flag without consuming it.</summary>
    void ClearQueryInvalidated();
}