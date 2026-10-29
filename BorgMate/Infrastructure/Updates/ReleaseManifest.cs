namespace BorgMate.Infrastructure.Updates;

/// <summary>
///     Signed description of one platform's Velopack feed.
/// </summary>
public class ReleaseManifest
{
    public required int FormatVersion { get; init; }

    public required string AppId { get; init; }

    /// <summary>Release directory on the update server, for example <c>win-arm64</c>.</summary>
    public required string Platform { get; init; }

    public required string Channel { get; init; }

    /// <summary>Hex SHA-256 of the feed file bytes.</summary>
    public required string FeedSha256 { get; init; }
}
