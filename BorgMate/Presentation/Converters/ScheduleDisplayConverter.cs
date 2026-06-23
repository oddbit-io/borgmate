using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Domain.Repositories;
using BorgMate.Presentation.ViewModels;
using BorgMate.Runtime.Scheduling;
using Humanizer;

namespace BorgMate.Presentation.Converters;

/// <summary>MultiValueConverter: (repository, IsScheduled, LastBackupAt) -> schedule display text.</summary>
public class ScheduleDisplayConverter : IMultiValueConverter
{
    public static readonly ScheduleDisplayConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 1) return null;

        var repo = values[0] switch
        {
            RepositoryViewModel vm => vm.Model,
            BorgRepository model => model,
            _ => null
        };
        if (repo is null) return null;
        if (!repo.IsScheduled) return Strings.Get("Mode.Manual");

        var scheduleText = FormatSchedule(repo.Schedule);
        var nextRun = BackupScheduler.ComputeNextRun(repo);
        var next = HumanizeNextRun(nextRun);
        return $"{scheduleText} · {next}";
    }

    internal static string FormatSchedule(BackupSchedule s)
    {
        var c = Strings.Culture;
        var time = $"{s.Hour:D2}:{s.Minute:D2}";
        return s.Frequency switch
        {
            ScheduleFrequency.EveryNHours => string.Format(Strings.Get("ScheduleEveryNHours"),
                TimeSpan.FromHours(s.IntervalHours).Humanize(culture: c)),
            ScheduleFrequency.Daily => string.Format(Strings.Get("ScheduleDaily"), time),
            ScheduleFrequency.Weekly => string.Format(Strings.Get("ScheduleWeekly"),
                Strings.Get($"Dow.{s.DayOfWeek}"), time),
            ScheduleFrequency.Monthly => string.Format(Strings.Get("ScheduleMonthly"),
                s.DayOfMonth, time),
            _ => ""
        };
    }

    private static string HumanizeNextRun(DateTime? nextRun)
    {
        if (nextRun is null) return "—";
        var remaining = nextRun.Value - DateTime.Now;
        if (remaining <= TimeSpan.Zero) return Strings.Get("Schedule.Now");
        var text = remaining.Humanize(2, minUnit: TimeUnit.Minute, culture: Strings.Culture);
        return string.Format(Strings.Get("Schedule.InTime"), text);
    }
}