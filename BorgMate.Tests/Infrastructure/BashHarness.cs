using System.Diagnostics;

namespace BorgMate.Tests.Infrastructure;

/// <summary>
///     Runs wsl.exe argv lists through a real local bash: everything up to and including --exec is dropped,
///     so the script and its positional parameters execute exactly as the distro would execute them.
/// </summary>
internal static class BashHarness
{
    private const string ExecFlag = "--exec";

    public static bool IsAvailable => FindOnPath("bash") is not null;

    /// <summary>
    ///     Skips the test on Windows when Git Bash is missing and fails it on other systems, where bash is a hard
    ///     requirement.
    /// </summary>
    public static void RequireBash()
    {
        if (IsAvailable)
            return;

        if (OperatingSystem.IsWindows())
            throw new SkipException("Git Bash is not on PATH.");

        Assert.Fail("bash is required on this operating system.");
    }

    public static string? FindOnPath(string program)
    {
        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // System32\bash.exe launches WSL; local tests need a native bash.
            if (directory.Contains("\\Windows\\System32", StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = Path.Combine(directory, program + extension);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static async Task<BashRunResult> RunWslArgumentsAsync(
        IReadOnlyList<string> wslArguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var execIndex = wslArguments.ToList().IndexOf(ExecFlag);
        Assert.True(execIndex >= 0);
        var program = wslArguments[execIndex + 1];
        Assert.Equal("bash", program);

        var bash = FindOnPath(program);
        Assert.NotNull(bash);

        var psi = new ProcessStartInfo
        {
            FileName = bash,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in wslArguments.Skip(execIndex + 2))
            psi.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new BashRunResult { ExitCode = process.ExitCode, Stdout = stdout };
    }
}
