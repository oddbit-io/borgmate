using System;
using System.Collections.Generic;

namespace BorgMate.Infrastructure.Borg;

public record BorgResult(int ExitCode, string StandardOutput, string StandardError, bool WasCancelled = false)
{
    private const int MaxErrorMessageLines = 8;

    public bool Success => ExitCode == 0;

    public BorgModernExitCode? ErrorCode => Success || WasCancelled
        ? null
        : BorgModernExitCodeMapper.TryMap(ExitCode);

    /// <summary>
    ///     User-friendly error message from the modern exit code, with stderr diagnostic detail preserved.
    /// </summary>
    public string ErrorMessage => BorgErrorMessages.GetMessage(ErrorCode, ExitCode, ExtractErrorMessage(StandardError));

    private static string ExtractErrorMessage(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return string.Empty;

        var diagnosticLines = new List<string>();
        var fallbackLines = new List<string>();

        foreach (var rawLine in stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            fallbackLines.Add(line);
            if (!IsBorgProgressNoise(line))
                diagnosticLines.Add(line);
        }

        var lines = diagnosticLines.Count > 0 ? diagnosticLines : fallbackLines;
        return string.Join(Environment.NewLine, TakeLast(lines, MaxErrorMessageLines));
    }

    private static bool IsBorgProgressNoise(string line)
    {
        return line.StartsWith("Initializing cache transaction:", StringComparison.Ordinal)
               || IsCreateProgressLine(line);
    }

    private static bool IsCreateProgressLine(string line)
    {
        var padded = $" {line} ";
        return padded.Contains(" O ", StringComparison.Ordinal)
               && padded.Contains(" N ", StringComparison.Ordinal)
               && (padded.Contains(" C ", StringComparison.Ordinal)
                   || padded.Contains(" D ", StringComparison.Ordinal)
                   || padded.Contains(" U ", StringComparison.Ordinal));
    }

    private static IEnumerable<string> TakeLast(IReadOnlyList<string> lines, int count)
    {
        var start = Math.Max(0, lines.Count - count);
        for (var i = start; i < lines.Count; i++)
            yield return lines[i];
    }
}
