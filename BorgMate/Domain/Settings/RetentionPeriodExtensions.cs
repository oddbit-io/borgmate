using System;

namespace BorgMate.Domain.Settings;

public static class RetentionPeriodExtensions
{
    public static DateTime? CutoffDate(this RetentionPeriod period)
    {
        return period switch
        {
            RetentionPeriod.OneDay => DateTime.Now.AddDays(-1),
            RetentionPeriod.OneWeek => DateTime.Now.AddDays(-7),
            RetentionPeriod.OneMonth => DateTime.Now.AddMonths(-1),
            RetentionPeriod.OneYear => DateTime.Now.AddYears(-1),
            RetentionPeriod.Forever => null,
            _ => DateTime.Now.AddDays(-7)
        };
    }
}