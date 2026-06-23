using BorgMate.Domain.Repositories;
using BorgMate.Presentation.Converters;
using BackupSchedule = BorgMate.Domain.Repositories.BackupSchedule;

namespace BorgMate.Tests.Domain;

public class BackupScheduleTests
{
    [Fact]
    public void FormatSchedule_Daily()
    {
        var schedule = new BackupSchedule { Frequency = ScheduleFrequency.Daily, Hour = 2, Minute = 30 };
        Assert.Contains("02:30", ScheduleDisplayConverter.FormatSchedule(schedule));
    }

    [Fact]
    public void FormatSchedule_Weekly()
    {
        var schedule = new BackupSchedule
        {
            Frequency = ScheduleFrequency.Weekly,
            Hour = 3, Minute = 0, DayOfWeek = DayOfWeek.Monday
        };
        Assert.Contains("03:00", ScheduleDisplayConverter.FormatSchedule(schedule));
    }

    [Fact]
    public void FormatSchedule_Monthly()
    {
        var schedule = new BackupSchedule
        {
            Frequency = ScheduleFrequency.Monthly,
            Hour = 4, Minute = 0, DayOfMonth = 15
        };
        var display = ScheduleDisplayConverter.FormatSchedule(schedule);
        Assert.Contains("04:00", display);
        Assert.Contains("15", display);
    }

    [Fact]
    public void FormatSchedule_EveryNHours()
    {
        var schedule = new BackupSchedule
        {
            Frequency = ScheduleFrequency.EveryNHours,
            Minute = 0, IntervalHours = 6
        };
        Assert.NotEmpty(ScheduleDisplayConverter.FormatSchedule(schedule));
    }
}
