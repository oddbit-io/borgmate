using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Keychain;

public abstract class KeychainServiceBase(ILogger logger)
{
    protected const string ServiceName = "BorgMate";
    protected ILogger Logger => logger;

    protected static string SanitizeAccount(string repoPath) =>
        repoPath.Replace("\"", "").Replace("'", "").Replace("\\", "/");

    protected static string EscapeShell(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string RedactArguments(string arguments)
    {
        // Redact macOS: -w "password" or -w password
        arguments = System.Text.RegularExpressions.Regex.Replace(arguments, @"-w\s+\S+", "-w ***");
        // Redact Windows: /pass:"password" or /pass:password
        arguments = System.Text.RegularExpressions.Regex.Replace(arguments, @"/pass:\S+", "/pass:***");
        return arguments;
    }

    protected record ProcessResult(int ExitCode, string Output);

    protected async Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdinData = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinData is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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
        await process.WaitForExitAsync();

        logger.LogDebug("Keychain: {FileName} {Command} -> exit {ExitCode}",
            fileName, RedactArguments(arguments), process.ExitCode);

        return new ProcessResult(process.ExitCode, output);
    }
}
