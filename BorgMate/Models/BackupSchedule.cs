using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Models;

public partial class BackupSchedule : ObservableObject
{
    [ObservableProperty]
    private ScheduleFrequency _frequency = ScheduleFrequency.Daily;

    [ObservableProperty]
    private int _hour = 2;

    [ObservableProperty]
    private int _minute;

    [ObservableProperty]
    private DayOfWeek _dayOfWeek = DayOfWeek.Monday;

    [ObservableProperty]
    private int _dayOfMonth = 1;

    /// <summary>Hours between backups. Only used when Frequency is EveryNHours.</summary>
    [ObservableProperty]
    private int _intervalHours = 6;

    /// <summary>
    /// When true, backups that were scheduled while the app was closed run immediately on startup.
    /// When false, ComputeNextRun advances past-due times to the next future occurrence.
    /// </summary>
    [ObservableProperty]
    private bool _runMissed = true;
}
