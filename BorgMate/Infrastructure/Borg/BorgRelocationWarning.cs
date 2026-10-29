using System.Text.RegularExpressions;
using BorgMate.Domain.Repositories;

namespace BorgMate.Infrastructure.Borg;

/// <summary>
///     Reads the warning Borg prints before it exits with
///     <see cref="BorgModernExitCode.RepositoryAccessAborted" /> for a relocated repository.
/// </summary>
public static partial class BorgRelocationWarning
{
    [GeneratedRegex(
        @"^Warning: The repository at location (?<location>[^\r\n]+) was previously located at (?<previous>[^\r\n]+)\r?$",
        RegexOptions.Multiline)]
    private static partial Regex WarningPattern();

    /// <summary>Returns the reported relocation, or null when <paramref name="stderr" /> has no relocation warning.</summary>
    public static RepositoryRelocation? TryParse(string stderr)
    {
        var match = WarningPattern().Match(stderr);
        return match.Success
            ? new RepositoryRelocation
            {
                Location = match.Groups["location"].Value,
                PreviousLocation = match.Groups["previous"].Value
            }
            : null;
    }
}
