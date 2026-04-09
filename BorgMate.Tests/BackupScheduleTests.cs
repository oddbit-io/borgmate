using BorgMate.Models;

namespace BorgMate.Tests;

public class BackupScheduleTests
{
    [Fact]
    public void ScheduleDisplay_Daily()
    {
        var schedule = new BackupSchedule { Frequency = ScheduleFrequency.Daily, Hour = 2, Minute = 30 };
        Assert.Contains("02:30", schedule.ScheduleDisplay);
    }

    [Fact]
    public void ScheduleDisplay_Weekly()
    {
        var schedule = new BackupSchedule
        {
            Frequency = ScheduleFrequency.Weekly,
            Hour = 3, Minute = 0, DayOfWeek = DayOfWeek.Monday
        };
        var display = schedule.ScheduleDisplay;
        Assert.Contains("03:00", display);
    }

    [Fact]
    public void ScheduleDisplay_Monthly()
    {
        var schedule = new BackupSchedule
        {
            Frequency = ScheduleFrequency.Monthly,
            Hour = 4, Minute = 0, DayOfMonth = 15
        };
        var display = schedule.ScheduleDisplay;
        Assert.Contains("04:00", display);
        Assert.Contains("15", display);
    }

    [Fact]
    public void ScheduleDisplay_EveryNHours()
    {
        var schedule = new BackupSchedule
        {
            Frequency = ScheduleFrequency.EveryNHours,
            Minute = 0, IntervalHours = 6
        };
        Assert.NotEmpty(schedule.ScheduleDisplay);
    }
}
