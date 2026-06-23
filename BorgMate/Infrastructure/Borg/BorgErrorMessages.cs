using System;

namespace BorgMate.Infrastructure.Borg;

/// <summary>
///     Provides user-facing error messages for exact Borg modern exit codes.
///     Uses Strings.Get() for localized messages.
/// </summary>
public static class BorgErrorMessages
{
    /// <summary>Returns the localized headline for an exact Borg modern exit code.</summary>
    public static string GetMessage(BorgModernExitCode code)
    {
        return Strings.Get($"Error.BorgModernExitCode.{code}");
    }

    /// <summary>Returns a localized Borg failure headline with stderr diagnostic detail appended.</summary>
    public static string GetMessage(BorgModernExitCode? code, int exitCode, string detail)
    {
        var headline = code is { } value
            ? GetMessage(value)
            : exitCode > 0
                ? string.Format(Strings.Get("Error.UnknownBorgExitCode"), exitCode)
                : string.Empty;

        if (string.IsNullOrWhiteSpace(headline))
            return detail;

        return string.IsNullOrWhiteSpace(detail)
            ? headline
            : $"{headline}{Environment.NewLine}{Environment.NewLine}{detail}";
    }
}
