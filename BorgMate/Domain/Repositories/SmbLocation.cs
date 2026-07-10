using System;
using System.Linq;

namespace BorgMate.Domain.Repositories;

/// <summary>
///     Typed SMB share location parsed from an "smb://[user@]host/share[/path]" repository path.
///     Passwords are never part of the URL; they live in the platform keychain under <see cref="KeychainKey" />.
/// </summary>
public sealed record SmbLocation
{
    /// <summary>SMB server host name or IP address.</summary>
    public required string Host { get; init; }

    /// <summary>Share name as exported by the server.</summary>
    public required string Share { get; init; }

    /// <summary>Repository path inside the share; empty when the repository is the share root.</summary>
    public string PathInShare { get; init; } = string.Empty;

    /// <summary>Account used to connect; empty means guest/anonymous access.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Keychain entry key for the share password.</summary>
    public string KeychainKey => $"smb:{Host.ToLowerInvariant()}/{Share.ToLowerInvariant()}";

    /// <summary>Share identity shown in prompts and error messages.</summary>
    public string DisplayName => $"//{Host}/{Share}";

    /// <summary>Parses an "smb://[user@]host/share[/path]" URL; returns null when the value is not one.</summary>
    public static SmbLocation? TryParse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "smb", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(uri.Host))
            return null;

        // Embedded passwords are rejected: URLs are persisted to config and may appear in logs.
        if (uri.UserInfo.Contains(':', StringComparison.Ordinal))
            return null;

        var segments = uri.AbsolutePath.Trim('/').Split('/', 2);
        var share = Uri.UnescapeDataString(segments[0]);
        if (share.Length == 0)
            return null;

        return new SmbLocation
        {
            Host = uri.Host,
            Share = share,
            PathInShare = segments.Length > 1 ? Uri.UnescapeDataString(segments[1]) : string.Empty,
            Username = Uri.UnescapeDataString(uri.UserInfo)
        };
    }

    /// <summary>Builds the canonical "smb://[user@]host/share[/path]" URL for persistence.</summary>
    public string ToUrl()
    {
        var user = Username.Length == 0 ? string.Empty : $"{Uri.EscapeDataString(Username)}@";
        var path = PathInShare.Length == 0 ? string.Empty : $"/{EscapePath(PathInShare)}";
        return $"smb://{user}{Host}/{EscapePath(Share)}{path}";
    }

    private static string EscapePath(string path)
    {
        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }
}
