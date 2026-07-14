using System.Collections.Generic;

namespace BorgMate.Infrastructure.Platform;

/// <summary>Prepared SSH options shared by Borg and direct remote commands.</summary>
public class PreparedSshConnection
{
    public required string BorgRsh { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public List<string>? WslPreCommands { get; init; }
}
