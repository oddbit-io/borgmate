using System;
using System.IO;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Resolves a bare program name through the absolute directories on PATH. Process.Start looks in
///     the application directory and the current directory first, where a planted file of the same
///     name would run instead of the system tool.
/// </summary>
public static class ExecutableResolver
{
    /// <summary>Returns <paramref name="fileName" /> unchanged when it already names a directory.</summary>
    /// <exception cref="FileNotFoundException">No PATH directory contains the program.</exception>
    public static string Resolve(string fileName)
    {
        if (Path.IsPathRooted(fileName) || fileName.IndexOfAny(['/', '\\']) >= 0)
            return fileName;

        var name = OperatingSystem.IsWindows() && !Path.HasExtension(fileName) ? fileName + ".exe" : fileName;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // A relative entry such as "." would bring the current directory back.
            if (!Path.IsPathFullyQualified(directory))
                continue;

            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"'{fileName}' was not found in any PATH directory.", fileName);
    }
}
