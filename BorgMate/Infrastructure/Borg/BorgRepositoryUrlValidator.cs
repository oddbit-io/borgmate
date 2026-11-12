using System;
using System.Text.RegularExpressions;
using BorgMate.Infrastructure.Platform;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Validates and redacts Borg 2 repository URLs accepted by BorgMate.</summary>
public static partial class BorgRepositoryUrlValidator
{
    private const string RclonePrefix = "rclone:";

    /// <summary>Returns true when <paramref name="value" /> is a supported Borg 2 repository URL.</summary>
    public static bool IsSupportedBorg2Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith(RclonePrefix, StringComparison.OrdinalIgnoreCase))
            return IsConfiguredRcloneRemote(trimmed[RclonePrefix.Length..]) && !HasEmbeddedSecret(trimmed);

        if (trimmed.StartsWith("s3:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("b2:", StringComparison.OrdinalIgnoreCase))
            return !HasEmbeddedSecret(trimmed);

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || HasEmbeddedSecret(trimmed))
            return false;

        return uri.Scheme switch
        {
            "ssh" or "sftp" => SshDestination.IsValid(Uri.UnescapeDataString(uri.UserInfo), uri.Host),
            "file" or "rest" => true,
            _ => false
        };
    }

    /// <summary>Returns true for URLs whose transport can use SSH setup options.</summary>
    public static bool UsesSshTransport(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme is "ssh" or "sftp";
    }

    /// <summary>Returns true when a URL user-info segment contains a password or secret.</summary>
    public static bool HasEmbeddedSecret(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
            return uri.UserInfo.Contains(':', StringComparison.Ordinal);

        var schemeIndex = value.IndexOf(':', StringComparison.Ordinal);
        var atIndex = value.IndexOf('@', StringComparison.Ordinal);
        if (schemeIndex < 0 || atIndex <= schemeIndex)
            return false;

        var userInfo = value[(schemeIndex + 1)..atIndex].TrimStart('/');
        return userInfo.Contains(':', StringComparison.Ordinal);
    }

    /// <summary>Redacts URL user-info values before command logging or debug display.</summary>
    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
            return value.Replace($"{uri.UserInfo}@", "***@", StringComparison.Ordinal);

        var schemeIndex = value.IndexOf(':', StringComparison.Ordinal);
        var atIndex = value.IndexOf('@', StringComparison.Ordinal);
        if (schemeIndex < 0 || atIndex <= schemeIndex)
            return value;

        var prefixEnd = schemeIndex + 1;
        while (prefixEnd < atIndex && value[prefixEnd] == '/')
            prefixEnd++;

        return string.Concat(value.AsSpan(0, prefixEnd), "***", value.AsSpan(atIndex));
    }

    /// <summary>
    ///     Accepts only <c>remote:path</c> with a remote from the rclone config. On-the-fly remotes
    ///     (<c>:backend,…</c>) and connection-string parameters (<c>remote,ssh='…'</c>) can run commands.
    /// </summary>
    private static bool IsConfiguredRcloneRemote(string location)
    {
        var colonIndex = location.IndexOf(':', StringComparison.Ordinal);
        return colonIndex > 0 && RcloneRemoteNamePattern().IsMatch(location[..colonIndex]);
    }

    [GeneratedRegex(@"^[\w.+@][\w.+@ -]*$")]
    private static partial Regex RcloneRemoteNamePattern();
}
