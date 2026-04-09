using System;
using BorgMate.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;

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

    /// <summary>Localized human-readable schedule text built from the current field values.</summary>
    public string ScheduleDisplay => BuildDisplay();

    partial void OnFrequencyChanged(ScheduleFrequency value) => NotifyComputed();
    partial void OnHourChanged(int value) => NotifyComputed();
    partial void OnMinuteChanged(int value) => NotifyComputed();
    partial void OnDayOfWeekChanged(DayOfWeek value) => NotifyComputed();
    partial void OnDayOfMonthChanged(int value) => NotifyComputed();
    partial void OnIntervalHoursChanged(int value) => NotifyComputed();

    private void NotifyComputed()
    {
        OnPropertyChanged(nameof(ScheduleDisplay));
    }

    private string BuildDisplay()
    {
        var culture = Strings.Culture;
        var time = $"{Hour:D2}:{Minute:D2}";
        return Frequency switch
        {
            ScheduleFrequency.EveryNHours => string.Format(Strings.Get("ScheduleEveryNHours"),
                TimeSpan.FromHours(IntervalHours).Humanize(culture: culture)),
            ScheduleFrequency.Daily => string.Format(Strings.Get("ScheduleDaily"), time),
            ScheduleFrequency.Weekly => string.Format(Strings.Get("ScheduleWeekly"),
                Strings.Get($"Dow.{DayOfWeek}"), time),
            ScheduleFrequency.Monthly => string.Format(Strings.Get("ScheduleMonthly"),
                DayOfMonth, time),
            _ => ""
        };
    }
}
