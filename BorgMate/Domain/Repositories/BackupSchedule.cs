using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Domain.Repositories;

public partial class BackupSchedule : ObservableObject
{
    [ObservableProperty]
    private int _dayOfMonth = 1;

    [ObservableProperty]
    private DayOfWeek _dayOfWeek = DayOfWeek.Monday;

    [ObservableProperty]
    private ScheduleFrequency _frequency = ScheduleFrequency.Daily;

    [ObservableProperty]
    private int _hour = 2;

    /// <summary>Hours between backups. Only used when Frequency is EveryNHours.</summary>
    [ObservableProperty]
    private int _intervalHours = 6;

    [ObservableProperty]
    private int _minute;

    /// <summary>
    ///     When true, backups that were scheduled while the app was closed run immediately on startup.
    ///     When false, ComputeNextRun advances past-due times to the next future occurrence.
    /// </summary>
    [ObservableProperty]
    private bool _runMissed = true;

    /// <summary>
    ///     When true, a successful backup automatically enqueues prune afterwards. Only honored
    ///     when the repo has at least one retention rule configured (PruneOptions.HasAnyRetention).
    /// </summary>
    [ObservableProperty]
    private bool _runPruneAfterBackup;
}