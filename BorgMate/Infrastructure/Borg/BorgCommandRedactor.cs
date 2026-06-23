using System;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Redacts command-line and environment values before log or debug display.</summary>
public static class BorgCommandRedactor
{
    /// <summary>Redacts user-info values in URI-like strings.</summary>
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
