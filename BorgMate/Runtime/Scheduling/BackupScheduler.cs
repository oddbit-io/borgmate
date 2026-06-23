using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Domain.Repositories;
using BorgMate.Persistence.Configuration;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Progress;
using Microsoft.Extensions.Logging;
using RepositoryPresentationState = BorgMate.Presentation.State.RepositoryPresentationState;

namespace BorgMate.Runtime.Scheduling;

/// <summary>
///     Checks scheduled repositories every minute and triggers backups when due.
/// </summary>
public class BackupScheduler : IBackupScheduler
{
    private readonly ILogger<BackupScheduler> _logger;
    private readonly IRepositoryOperationRunner _repositoryRunner;
    private readonly ISettingsStore _settingsStore;
    private readonly RepositoryPresentationState _store;
    private readonly DispatcherTimer _timer;
    private bool _isChecking;

    public BackupScheduler(
        ILogger<BackupScheduler> logger,
        RepositoryPresentationState store,
        IRepositoryOperationRunner repositoryRunner,
        ISettingsStore settingsStore)
    {
        _logger = logger;
        _store = store;
        _repositoryRunner = repositoryRunner;
        _settingsStore = settingsStore;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = CheckSchedulesGuardedAsync();
    }

    public void Start()
    {
        _timer.Start();
        // Check immediately for missed backups on startup
        _ = CheckSchedulesGuardedAsync();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private async Task CheckSchedulesGuardedAsync()
    {
        if (_isChecking) return;
        _isChecking = true;
        try
        {
            await CheckSchedulesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schedule check failed");
        }
        finally
        {
            _isChecking = false;
        }
    }

    private async Task CheckSchedulesAsync()
    {
        var now = DateTime.Now;

        foreach (var repo in _store.Repositories.Where(r => r.IsScheduled && !r.IsBusy))
        {
            var nextRun = ComputeNextRun(repo);
            if (nextRun is null || nextRun > now) continue;

            try
            {
                _logger.LogInformation("Scheduled backup triggered for '{Name}'", repo.Name);

                repo.IsBusy = true;
                var backup = await _repositoryRunner.BackupAsync(
                    new BackupOperationOptions(repo, NullOperationProgressSink.Instance));

                if (backup.IsT0 &&
                    repo.Schedule.RunPruneAfterBackup &&
                    repo.PruneOptions.HasAnyRetention)
                {
                    var prune = await _repositoryRunner.PruneAsync(
                        new PruneOperationOptions(
                            repo,
                            NullOperationProgressSink.Instance,
                            repo.PruneOptions.CompactAfterPrune));

                    if (prune.IsT0 && prune.AsT0.ShouldCompact)
                        await _repositoryRunner.CompactAsync(
                            new CompactOperationOptions(repo, NullOperationProgressSink.Instance));
                }

                _settingsStore.RequestSave();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled backup failed for '{Name}'", repo.Name);
            }
            finally
            {
                repo.IsBusy = false;
            }
        }
    }

    /// <summary>
    ///     Computes the next run time based on the schedule and last backup.
    ///     Returns null if the schedule is invalid.
    /// </summary>
    public static DateTime? ComputeNextRun(BorgRepository repo)
    {
        if (repo.Mode != BackupMode.Scheduled) return null;

        var schedule = repo.Schedule;
        var lastRun = repo.LastBackupAt ?? DateTime.MinValue;

        var next = schedule.Frequency switch
        {
            ScheduleFrequency.EveryNHours =>
                lastRun == DateTime.MinValue
                    ? DateTime.Now // Run immediately if never backed up
                    : lastRun.AddHours(schedule.IntervalHours),

            ScheduleFrequency.Daily =>
                NextOccurrence(lastRun, schedule.Hour, schedule.Minute, TimeSpan.FromDays(1)),

            ScheduleFrequency.Weekly =>
                NextWeeklyOccurrence(lastRun, schedule.DayOfWeek, schedule.Hour, schedule.Minute),

            ScheduleFrequency.Monthly =>
                NextMonthlyOccurrence(lastRun, schedule.DayOfMonth, schedule.Hour, schedule.Minute),

            _ => (DateTime?)null
        };

        // When RunMissed is disabled, advance past-due times to the next future occurrence
        if (next is not null && !schedule.RunMissed && next < DateTime.Now && lastRun != DateTime.MinValue)
            next = schedule.Frequency switch
            {
                ScheduleFrequency.EveryNHours =>
                    AdvanceToFuture(next.Value, TimeSpan.FromHours(schedule.IntervalHours)),
                ScheduleFrequency.Daily => AdvanceToFuture(next.Value, TimeSpan.FromDays(1)),
                ScheduleFrequency.Weekly => AdvanceToFuture(next.Value, TimeSpan.FromDays(7)),
                ScheduleFrequency.Monthly => AdvanceMonthlyToFuture(next.Value),
                _ => next
            };

        return next;
    }

    private static DateTime AdvanceToFuture(DateTime time, TimeSpan interval)
    {
        var now = DateTime.Now;
        while (time <= now)
            time += interval;
        return time;
    }

    private static DateTime AdvanceMonthlyToFuture(DateTime time)
    {
        var now = DateTime.Now;
        while (time <= now)
            time = time.AddMonths(1);
        return time;
    }

    private static DateTime NextOccurrence(DateTime lastRun, int hour, int minute, TimeSpan interval)
    {
        var today = DateTime.Today.AddHours(hour).AddMinutes(minute);
        if (today > lastRun) return today;
        return today.Add(interval);
    }

    private static DateTime NextWeeklyOccurrence(DateTime lastRun, DayOfWeek dayOfWeek, int hour, int minute)
    {
        var today = DateTime.Today;
        var daysUntil = ((int)dayOfWeek - (int)today.DayOfWeek + 7) % 7;
        var next = today.AddDays(daysUntil).AddHours(hour).AddMinutes(minute);
        if (next <= lastRun) next = next.AddDays(7);
        return next;
    }

    private static DateTime NextMonthlyOccurrence(DateTime lastRun, int dayOfMonth, int hour, int minute)
    {
        var now = DateTime.Now;
        var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(now.Year, now.Month));
        var next = new DateTime(now.Year, now.Month, day, hour, minute, 0);
        if (next <= lastRun) next = next.AddMonths(1);
        return next;
    }
}