using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Platform;
using Microsoft.Extensions.Logging;

namespace BorgMate.Persistence.Keychain;

public abstract class KeychainServiceBase(ILogger logger)
{
    protected const string ServiceName = StringHelpers.AppName;
    protected ILogger Logger => logger;

    protected static string SanitizeAccount(string repoPath)
    {
        return repoPath.Replace("\"", "").Replace("'", "").Replace("\\", "/");
    }

    private static string RedactArguments(IEnumerable<string> arguments)
    {
        var redacted = new List<string>();
        var redactNext = false;

        foreach (var argument in arguments)
        {
            if (redactNext)
            {
                redacted.Add("***");
                redactNext = false;
                continue;
            }

            if (argument == "-w")
            {
                redacted.Add(argument);
                redactNext = true;
                continue;
            }

            redacted.Add(Regex.Replace(argument, @"/pass:.+", "/pass:***"));
        }

        return string.Join(" ", redacted);
    }

    protected Task<ProcessResult> RunAsync(string fileName, params string[] arguments)
    {
        return RunAsync(fileName, stdinData: null, arguments);
    }

    protected Task<ProcessResult> RunWithStandardInputAsync(string fileName, string stdinData, params string[] arguments)
    {
        return RunAsync(fileName, stdinData, arguments);
    }

    private async Task<ProcessResult> RunAsync(string fileName, string? stdinData, string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinData is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["LC_ALL"] = "C";

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)!;

        if (stdinData is not null)
        {
            await process.StandardInput.WriteAsync(stdinData);
            process.StandardInput.Close();
        }

        // Read both streams concurrently to prevent deadlock when stderr
        // buffer fills (>64KB) while we're only reading stdout.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        var output = stdoutTask.Result;
        var error = stderrTask.Result;
        await process.WaitForExitAsync();

        logger.LogDebug("Keychain: {FileName} {Command} -> exit {ExitCode}",
            fileName, RedactArguments(arguments), process.ExitCode);

        return new ProcessResult(process.ExitCode, output, error);
    }

    protected record ProcessResult(int ExitCode, string Output, string Error);
}
