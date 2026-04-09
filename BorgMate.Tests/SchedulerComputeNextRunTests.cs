using BorgMate.Models;
using BorgMate.Services;

namespace BorgMate.Tests;

public class SchedulerComputeNextRunTests
{
    private static BorgRepository MakeRepo(ScheduleFrequency frequency, DateTime? lastBackup = null,
        int hour = 2, int minute = 0, DayOfWeek dayOfWeek = DayOfWeek.Monday,
        int dayOfMonth = 1, int intervalHours = 6, bool runMissed = true)
    {
        return new BorgRepository
        {
            Name = "test",
            Path = "/test",
            Mode = BackupMode.Scheduled,
            LastBackupAt = lastBackup,
            Schedule = new BackupSchedule
            {
                Frequency = frequency,
                Hour = hour,
                Minute = minute,
                DayOfWeek = dayOfWeek,
                DayOfMonth = dayOfMonth,
                IntervalHours = intervalHours,
                RunMissed = runMissed
            }
        };
    }

    [Fact]
    public void Manual_ReturnsNull()
    {
        var repo = new BorgRepository { Name = "test", Path = "/test", Mode = BackupMode.Manual };
        Assert.Null(SchedulerService.ComputeNextRun(repo));
    }

    [Fact]
    public void EveryNHours_NeverRun_ReturnsNow()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, intervalHours: 6);
        var next = SchedulerService.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.True((DateTime.Now - next.Value).Duration() < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EveryNHours_LastRunRecent_ReturnsLastPlusInterval()
    {
        var lastRun = DateTime.Now.AddHours(-2);
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, lastRun, intervalHours: 6);
        var next = SchedulerService.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.True((next.Value - lastRun.AddHours(6)).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Daily_ReturnsToday()
    {
        var repo = MakeRepo(ScheduleFrequency.Daily, DateTime.Now.AddDays(-2), hour: 2, minute: 30);
        var next = SchedulerService.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.Equal(2, next.Value.Hour);
        Assert.Equal(30, next.Value.Minute);
    }

    [Fact]
    public void Weekly_ReturnsCorrectDay()
    {
        var repo = MakeRepo(ScheduleFrequency.Weekly, DateTime.Now.AddDays(-14),
            dayOfWeek: DayOfWeek.Sunday, hour: 3);
        var next = SchedulerService.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Sunday, next.Value.DayOfWeek);
        Assert.Equal(3, next.Value.Hour);
    }

    [Fact]
    public void Monthly_ReturnsCorrectDayOfMonth()
    {
        var repo = MakeRepo(ScheduleFrequency.Monthly, DateTime.Now.AddMonths(-2),
            dayOfMonth: 15, hour: 4);
        var next = SchedulerService.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.Equal(4, next.Value.Hour);
        // Day should be 15 or clamped to month's max
        Assert.True(next.Value.Day <= 15);
    }

    [Fact]
    public void RunMissed_False_AdvancesToFuture()
    {
        // Last run was 3 days ago, daily at 2am, RunMissed=false
        var repo = MakeRepo(ScheduleFrequency.Daily, DateTime.Now.AddDays(-3),
            hour: 2, runMissed: false);
        var next = SchedulerService.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.True(next.Value > DateTime.Now, "Should be in the future when RunMissed=false");
    }

    [Fact]
    public void RunMissed_True_ReturnsPastDue()
    {
        // Last run was 3 days ago, daily at midnight, RunMissed=true (default).
        // Use hour=0 so today's occurrence is always in the past regardless of current time.
        var repo = MakeRepo(ScheduleFrequency.Daily, DateTime.Now.AddDays(-3), hour: 0, minute: 0);
        var next = SchedulerService.ComputeNextRun(repo);

        Assert.NotNull(next);
        // Past-due time should NOT be advanced, allowing immediate run
        Assert.True(next.Value < DateTime.Now);
    }
}
