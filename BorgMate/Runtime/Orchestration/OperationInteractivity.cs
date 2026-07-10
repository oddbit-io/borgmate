using System.Threading;

namespace BorgMate.Runtime.Orchestration;

/// <summary>
///     Ambient flag telling credential prompts whether a user is expected at the screen.
///     Set per borg operation by the orchestrator (it runs inside the job worker flow,
///     so the value reaches the mounter, ssh-agent, and passphrase provider via AsyncLocal).
///     Scheduled operations run non-interactive: prompts fail fast instead of blocking.
/// </summary>
public static class OperationInteractivity
{
    // Inverted so the AsyncLocal default (false) means interactive.
    private static readonly AsyncLocal<bool> NonInteractive = new();

    public static bool IsInteractive
    {
        get => !NonInteractive.Value;
        set => NonInteractive.Value = !value;
    }
}
