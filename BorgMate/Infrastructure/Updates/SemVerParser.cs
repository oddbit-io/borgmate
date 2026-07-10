using System;
using System.Text.RegularExpressions;
using Velopack;

namespace BorgMate.Infrastructure.Updates;

internal static partial class SemVerParser
{
    public static SemanticVersion Parse(string value)
    {
        if (!VersionPattern().IsMatch(value))
            throw new FormatException($"Invalid SemVer 2.0.0 version: {value}");

        return SemanticVersion.Parse(value);
    }

    public static bool TryParse(string value, out SemanticVersion? version)
    {
        if (!VersionPattern().IsMatch(value))
        {
            version = null;
            return false;
        }

        return SemanticVersion.TryParse(value, out version);
    }

    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
