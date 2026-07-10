using System;

namespace BorgMate.Runtime.Security;

/// <summary>
///     A credential prompt was needed during a non-interactive (scheduled) operation.
///     The message is already localized and user-facing; the job queue and orchestrator
///     surface it through the journal and notifications.
/// </summary>
public class CredentialsRequiredException : Exception
{
    public CredentialsRequiredException(string message)
        : base(message)
    {
    }
}
