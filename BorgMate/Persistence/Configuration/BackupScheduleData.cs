using BorgMate.Domain.Repositories;

namespace BorgMate.Persistence.Configuration;

public class BackupScheduleData
{
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
    public int Hour { get; set; } = 2;
    public int Minute { get; set; }
    public string DayOfWeek { get; set; } = "Monday";
    public int DayOfMonth { get; set; } = 1;
    public int IntervalHours { get; set; } = 6;
    public bool RunMissed { get; set; } = true;
    public bool RunPruneAfterBackup { get; set; }
}