namespace BorgMate.Infrastructure.Platform;

public class WakeScheduleResult
{
    private WakeScheduleResult(string? errorMessage)
    {
        ErrorMessage = errorMessage;
    }

    public string? ErrorMessage { get; }

    public bool IsSuccess => ErrorMessage is null;

    public static WakeScheduleResult Ok()
    {
        return new WakeScheduleResult(null);
    }

    public static WakeScheduleResult Failed(string errorMessage)
    {
        return new WakeScheduleResult(errorMessage);
    }
}
