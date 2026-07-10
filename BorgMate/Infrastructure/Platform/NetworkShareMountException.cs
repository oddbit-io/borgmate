using System;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     SMB share mount failure. The message is already localized and user-facing;
///     the job queue and orchestrator surface it through the journal and notifications.
/// </summary>
public class NetworkShareMountException : Exception
{
    public NetworkShareMountException(string message)
        : base(message)
    {
    }
}
