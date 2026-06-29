using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Presentation.ViewModels;
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
    /// <summary>
    ///     A due run that is only recently past (wake latency, timer granularity) is "now",
    ///     not "missed" — without this grace, RunMissed=false repositories can never fire.
    /// </summary>
    internal static readonly TimeSpan MissedRunGrace = TimeSpan.FromMinutes(10);

    private readonly ILogger<BackupScheduler> _logger;
    private readonly IRepositoryOperationRunner _repositoryRunner;
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly RepositoryPresentationState _store;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _wakeRefreshGate = new(1, 1);
    private readonly IWakeScheduler _wakeScheduler;
    private readonly IPowerSource _powerSource;
    private readonly IJournalStore _journal;
    private bool _isChecking;
    private bool _batteryDeferralReported;

    // Wake-state cache. _wakeSynced=false → OS state unknown, next refresh always applies.
    // _appliedWake is meaningful only while synced; null = cleared. The AC-only flag is part
    // of the key so toggling it re-applies the OS artifacts even when the time is unchanged.
    private bool _wakeSynced;
    private (DateTime WakeAt, bool AcOnly)? _appliedWake;
    private bool _lastWakeAttemptFailed;
    private (DateTime WakeAt, bool AcOnly)? _lastWakeTarget;

    public BackupScheduler(
        ILogger<BackupScheduler> logger,
        RepositoryPresentationState store,
        IRepositoryOperationRunner repositoryRunner,
        ISettingsStore settingsStore,
        AppSettings settings,
        IWakeScheduler wakeScheduler,
        IPowerSource powerSource,
        IJournalStore journal)
    {
        _logger = logger;
        _store = store;
        _repositoryRunner = repositoryRunner;
        _settingsStore = settingsStore;
        _settings = settings;
        _wakeScheduler = wakeScheduler;
        _powerSource = powerSource;
        _journal = journal;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = CheckSchedulesGuardedAsync();
        _settingsStore.Changed += snapshot => _ = RefreshWakeScheduleAsync(false, false);
    }

    public void Start()
    {
        _timer.Start();
        _ = RefreshWakeScheduleAsync(true, false);
        // Check immediately for missed backups on startup
        _ = CheckSchedulesGuardedAsync();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void RequestImmediateCheck()
    {
        _ = CheckSchedulesGuardedAsync();
    }

    public Task RefreshWakeScheduleAsync(bool interactive)
    {
        return RefreshWakeScheduleAsync(true, interactive);
    }

    internal async Task CheckSchedulesGuardedAsync()
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
            _ = RefreshWakeScheduleAsync(false, false);
        }
    }

    private async Task CheckSchedulesAsync()
    {
        var now = DateTime.Now;

        var due = _store.Repositories
            .Where(r => r.IsScheduled && !r.IsBusy)
            .Select(r => (Repo: r, PlannedAt: ComputeNextRun(r)))
            .Where(d => d.PlannedAt is not null && d.PlannedAt <= now)
            .Select(d => (d.Repo, PlannedAt: d.PlannedAt!.Value))
            .ToList();

        if (due.Count == 0)
            return;

        // Probed lazily — only when a run is actually due (on macOS the probe spawns
        // pmset; don't pay that every minute). Deferred runs execute under the normal
        // missed-run rules once AC power returns.
        if (_settings.ScheduledBackupsOnlyOnAcPower && await _powerSource.IsOnBatteryAsync())
        {
            ReportBatteryDeferral(due);
            return;
        }

        _batteryDeferralReported = false;

        foreach (var (repo, _) in due)
        {
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
    ///     Journal + OS notification, once per continuous battery episode — the check runs
    ///     every minute and must not spam. The flag resets when a due run proceeds on AC.
    /// </summary>
    private void ReportBatteryDeferral(IReadOnlyList<(RepositoryViewModel Repo, DateTime PlannedAt)> due)
    {
        _logger.LogInformation("Deferring scheduled backups: on battery with AC-only backups enabled");
        if (_batteryDeferralReported)
            return;

        _batteryDeferralReported = true;
        var lines = due.Select(d => string.Format(
            Strings.Get("BackupDeferred.Item"),
            d.Repo.Name,
            d.PlannedAt.ToString("g", Strings.Culture)));
        var detail = string.Join(Environment.NewLine, lines.Append(Strings.Get("BackupDeferred.Detail")));

        var entry = _journal.Add(
            JournalEventKind.BackupDeferred,
            repositoryName: due.Count == 1 ? due[0].Repo.Name : null);
        // Complete triggers the OS notification (honoring the ShowNotifications setting).
        _journal.Complete(entry, JournalResult.Deferred, detail);
    }

    internal async Task RefreshWakeScheduleAsync(bool force, bool interactive)
    {
        // Compute the target on the caller thread — _store is UI-thread-owned.
        var enabled = _settings.WakeForScheduledBackups && _wakeScheduler.IsSupported;
        var nextWake = enabled
            ? ComputeNextWakeRun(_store.Repositories.Select(r => r.Model), DateTime.Now)
            : null;
        (DateTime WakeAt, bool AcOnly)? target = nextWake is null
            ? null
            : (nextWake.Value, _settings.ScheduledBackupsOnlyOnAcPower);

        // Process work happens off the caller thread; the semaphore serializes refreshes.
        await _wakeRefreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ApplyWakeTargetAsync(target, force, interactive).ConfigureAwait(false);
        }
        finally
        {
            _wakeRefreshGate.Release();
        }
    }

    private async Task ApplyWakeTargetAsync((DateTime WakeAt, bool AcOnly)? target, bool force, bool interactive)
    {
        if (_wakeSynced && _appliedWake == target)
            return;
        if (_lastWakeAttemptFailed && _lastWakeTarget == target && !force)
            return;

        _lastWakeTarget = target;

        var result = target is null
            ? await _wakeScheduler.ClearAsync(interactive).ConfigureAwait(false)
            : await _wakeScheduler.ScheduleAsync(target.Value.WakeAt, interactive, target.Value.AcOnly)
                .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _wakeSynced = true;
            _appliedWake = target;
            _lastWakeAttemptFailed = false;
            return;
        }

        _lastWakeAttemptFailed = true;
        if (target is null)
        {
            _wakeSynced = false;
            _logger.LogWarning("Failed to clear system wake schedule: {Error}", result.ErrorMessage);
            return;
        }

        _logger.LogWarning("Failed to schedule system wake for {WakeAt}: {Error}",
            target.Value.WakeAt, result.ErrorMessage);

        // A stale OS wake may remain (previous target, or a partial apply) — try to leave
        // a clean state so the machine doesn't wake at the wrong moment.
        var clear = await _wakeScheduler.ClearAsync(interactive).ConfigureAwait(false);
        if (clear.IsSuccess)
        {
            _wakeSynced = true;
            _appliedWake = null;
        }
        else
        {
            _wakeSynced = false;
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

        // When RunMissed is disabled, advance past-due times to the next future occurrence.
        // The grace window keeps a just-due run (wake latency, timer granularity) eligible.
        if (next is not null && !schedule.RunMissed && next < DateTime.Now - MissedRunGrace && lastRun != DateTime.MinValue)
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

    internal static DateTime? ComputeNextWakeRun(IEnumerable<BorgRepository> repositories, DateTime now)
    {
        DateTime? nextWake = null;
        foreach (var repo in repositories)
        {
            var nextRun = ComputeNextRun(repo);
            if (nextRun is null || nextRun <= now) continue;
            var wakeAt = TrimToSecond(nextRun.Value);
            if (wakeAt <= now) continue;
            if (nextWake is null || wakeAt < nextWake)
                nextWake = wakeAt;
        }

        return nextWake;
    }

    internal static DateTime TrimToSecond(DateTime value)
    {
        return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
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
