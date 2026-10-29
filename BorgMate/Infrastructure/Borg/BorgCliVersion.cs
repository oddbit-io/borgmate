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

    /// <summary>Returns true only for the supported Borg 2.0.0b21 release.</summary>
    public bool IsSupportedBorg2 =>
        this is { Major: 2, Minor: 0, Patch: 0, Beta: 21, IsDevelopment: false };

    /// <summary>Version number without the program name, such as <c>1.4.4</c> or <c>2.0.0b21</c>.</summary>
    public string VersionNumber => Beta is { } beta
        ? $"{Major}.{Minor}.{Patch}b{beta}"
        : $"{Major}.{Minor}.{Patch}";

    /// <summary>True for Borg 1 releases older than 1.4.5, which the next BorgMate release drops.</summary>
    public bool IsDeprecatedBorg1 => Major == 1 && !IsAtLeastBorg145;

    public bool Supports(BorgCliFeature feature) =>
        feature switch
        {
            BorgCliFeature.QuickStats or BorgCliFeature.ExcludeDataless => IsAtLeastBorg145,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null)
        };

    private bool IsAtLeastBorg145 => this is { Major: 1, Minor: > 4 } or { Major: 1, Minor: 4, Patch: >= 5 };
}
