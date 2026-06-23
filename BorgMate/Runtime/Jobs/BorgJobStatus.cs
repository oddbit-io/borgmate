namespace BorgMate.Runtime.Jobs;

/// <summary>Lifecycle: Pending -> Running -> Completed/Failed/Cancelled.</summary>
public enum BorgJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}