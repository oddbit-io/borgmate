using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Models;
using BorgMate.ViewModels;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services;

/// <summary>
/// Checks scheduled repositories every minute and triggers backups when due.
/// </summary>
public class SchedulerService : ISchedulerService
{
    private readonly DispatcherTimer _timer;
    private readonly ILogger<SchedulerService> _logger;
    private readonly PassphrasePrompt _passphrase;
    private RepositoryListViewModel? _repoList;
    private bool _isChecking;

    public SchedulerService(ILogger<SchedulerService> logger, PassphrasePrompt passphrase)
    {
        _logger = logger;
        _passphrase = passphrase;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = CheckSchedulesGuardedAsync();
    }

    public void Start(RepositoryListViewModel repoList)
    {
        _repoList = repoList;
        _timer.Start();
        // Check immediately for missed backups on startup
        _ = CheckSchedulesGuardedAsync();
    }

    public void Stop() => _timer.Stop();

    private async Task CheckSchedulesGuardedAsync()
    {
        if (_isChecking) return;
        _isChecking = true;
        try { await CheckSchedulesAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Schedule check failed"); }
        finally { _isChecking = false; }
    }

    private async Task CheckSchedulesAsync()
    {
        if (_repoList is null) return;

        var now = DateTime.Now;

        foreach (var repo in _repoList.Repositories.Where(r => r.IsScheduled && !r.IsBusy))
        {
            var nextRun = ComputeNextRun(repo);
            if (nextRun is null || nextRun > now) continue;

            try
            {
                _logger.LogInformation("Scheduled backup triggered for '{Name}'", repo.Name);

                if (!await _passphrase.EnsurePassphraseAsync(repo))
                {
                    _logger.LogWarning("Passphrase not available for scheduled backup of '{Name}', skipping",
                        repo.Name);
                    continue;
                }

                await _repoList.RunBackupForRepo(repo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled backup failed for '{Name}'", repo.Name);
            }
        }
    }

    /// <summary>
    /// Computes the next run time based on the schedule and last backup.
    /// Returns null if the schedule is invalid.
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
        {
            next = schedule.Frequency switch
            {
                ScheduleFrequency.EveryNHours => AdvanceToFuture(next.Value, TimeSpan.FromHours(schedule.IntervalHours)),
                ScheduleFrequency.Daily => AdvanceToFuture(next.Value, TimeSpan.FromDays(1)),
                ScheduleFrequency.Weekly => AdvanceToFuture(next.Value, TimeSpan.FromDays(7)),
                ScheduleFrequency.Monthly => AdvanceMonthlyToFuture(next.Value),
                _ => next
            };
        }

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
