using System;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Validates and redacts Borg 2 repository URLs accepted by BorgMate.</summary>
public static class BorgRepositoryUrlValidator
{
    /// <summary>Returns true when <paramref name="value" /> is a supported Borg 2 repository URL.</summary>
    public static bool IsSupportedBorg2Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("rclone:", StringComparison.OrdinalIgnoreCase))
            return trimmed.Length > "rclone:".Length && !HasEmbeddedSecret(trimmed);

        if (trimmed.StartsWith("s3:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("b2:", StringComparison.OrdinalIgnoreCase))
            return !HasEmbeddedSecret(trimmed);

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               uri.Scheme is "ssh" or "sftp" or "file" or "rest" &&
               !HasEmbeddedSecret(trimmed);
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
}
