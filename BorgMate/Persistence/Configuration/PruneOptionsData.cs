namespace BorgMate.Persistence.Configuration;

public class PruneOptionsData
{
    public int KeepLast { get; set; }
    public int KeepHourly { get; set; }
    public int KeepDaily { get; set; }
    public int KeepWeekly { get; set; }
    public int KeepMonthly { get; set; }
    public int KeepYearly { get; set; }
    public bool CompactAfterPrune { get; set; } = true;
}