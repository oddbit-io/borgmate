namespace BorgMate.Infrastructure.Platform;

/// <summary>Numeric user and group id of a Linux user inside WSL.</summary>
public class WslUserIds
{
    public required int Uid { get; init; }

    public required int Gid { get; init; }

    /// <summary>Parses <c>uid:gid</c> as printed by <c>id -u</c> and <c>id -g</c>.</summary>
    public static WslUserIds? Parse(string text)
    {
        var parts = text.Trim().Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var uid) && int.TryParse(parts[1], out var gid)
            ? new WslUserIds { Uid = uid, Gid = gid }
            : null;
    }
}
