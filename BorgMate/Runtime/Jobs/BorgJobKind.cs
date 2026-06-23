namespace BorgMate.Runtime.Jobs;

/// <summary>
///     Query jobs don't set repo.IsBusy and are auto-removed on completion.
///     Command jobs cancel pending/running query jobs for the same repo on enqueue.
/// </summary>
public enum BorgJobKind
{
    Query,
    Command
}