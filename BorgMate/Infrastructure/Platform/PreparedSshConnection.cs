using System.Collections.Generic;

namespace BorgMate.Infrastructure.Platform;

/// <summary>Prepared SSH options shared by Borg and direct remote commands.</summary>
public class PreparedSshConnection
{
    public required string BorgRsh { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public List<string>? WslPreCommands { get; init; }

    /// <summary>Variables the WSL pre-commands read at runtime; they hold secrets and must travel via WSLENV.</summary>
    public IReadOnlyDictionary<string, string>? WslEnvironment { get; init; }
}
