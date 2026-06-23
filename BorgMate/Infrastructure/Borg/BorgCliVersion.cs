using System;
using System.Text.RegularExpressions;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Parsed Borg CLI version information from <c>borg --version</c>.</summary>
public sealed partial record BorgCliVersion(
    int Major,
    int Minor,
    int Patch,
    int? Beta,
    bool IsDevelopment,
    string OriginalText)
{
    [GeneratedRegex(@"(?i)\bborg(?:backup)?\s+(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:b(?<beta>\d+))?(?:\.dev(?<dev>\d+))?")]
    private static partial Regex VersionPattern();

    /// <summary>Parses text returned by <c>borg --version</c>.</summary>
    public static BorgCliVersion? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = VersionPattern().Match(text.Trim());
        if (!match.Success)
            return null;

        var beta = match.Groups["beta"].Success
            ? int.Parse(match.Groups["beta"].Value)
            : (int?)null;

        return new BorgCliVersion(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value),
            beta,
            match.Groups["dev"].Success,
            text.Trim());
    }

    /// <summary>Borg 2 support starts with the 2.0.0b21 CLI shape.</summary>
    public bool IsSupportedBorg2 => Major == 2 && (Minor > 0 || Patch > 0 || Beta is null or >= 21);

    /// <summary>Returns true for Borg 2 builds that use the b22 repo-create encryption split.</summary>
    public bool UsesBorg2SplitEncryption => Major == 2 && (Minor > 0 || Patch > 0 || Beta is null or >= 22);
}
