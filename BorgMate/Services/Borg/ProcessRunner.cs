using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace BorgMate.Services.Borg;

public static class ProcessRunner
{
    public static Task<BorgResult> RunAsync(
        string fileName,
        string arguments,
        Dictionary<string, string>? environment = null,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        return RunAsync(fileName, arguments, environment, workingDirectory, null, ct);
    }

    /// <summary>
    /// Runs a process and captures stdout/stderr. On cancellation, kills the process tree
    /// and races stream reads against a cancellation TCS to avoid blocked pipe reads on macOS.
    /// </summary>
    /// <param name="onKill">Optional callback before process kill, e.g. for WSL pkill cleanup.</param>
    public static async Task<BorgResult> RunAsync(
        string fileName,
        string arguments,
        Dictionary<string, string>? environment,
        string? workingDirectory,
        Action<string>? onStderrLine,
        CancellationToken ct = default,
        Action<string>? onStdoutLine = null,
        Action<Process>? onKill = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;
        }

        try
        {
            using var process = Process.Start(psi)!;
            var cancelledTcs = new TaskCompletionSource();
            await using var reg = ct.Register(() =>
            {
                try
                {
                    onKill?.Invoke(process);
                    process.Kill(entireProcessTree: true);
                }
                catch { /* already exited */ }
                cancelledTcs.TrySetResult();
            });

            Task<string> stdoutTask;
            if (onStdoutLine is not null)
                stdoutTask = ReadStreamWithCallbackAsync(process.StandardOutput, onStdoutLine, ct);
            else
                stdoutTask = process.StandardOutput.ReadToEndAsync(ct);

            Task<string> stderrTask;
            if (onStderrLine is not null)
                stderrTask = ReadStreamWithCallbackAsync(process.StandardError, onStderrLine, ct);
            else
                stderrTask = process.StandardError.ReadToEndAsync(ct);

            // Race stream reads against cancellation — pipe reads may not finish
            // promptly after process kill on some platforms.
            var readTask = Task.WhenAll(stdoutTask, stderrTask);
            await Task.WhenAny(readTask, cancelledTcs.Task);

            if (ct.IsCancellationRequested)
                return new BorgResult(-1, "", "", WasCancelled: true);

            await readTask;
            await process.WaitForExitAsync(ct);

            return new BorgResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (Win32Exception)
        {
            return new BorgResult(-1, "", $"Binary not found: '{fileName}'. Check application settings.");
        }
        catch (OperationCanceledException)
        {
            return new BorgResult(-1, "", "", WasCancelled: true);
        }
    }

    /// <summary>
    /// Reads a stream, splitting on \r and \n, calling back per line.
    /// Returns the final accumulated text.
    /// </summary>
    private static async Task<string> ReadStreamWithCallbackAsync(
        StreamReader reader, Action<string> onLine, CancellationToken ct)
    {
        var fullOutput = new StringBuilder();
        var current = new StringBuilder();
        var buffer = new char[256];

        while (true)
        {
            var read = await reader.ReadAsync(buffer, ct);
            if (read == 0) break;

            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c is '\r' or '\n')
                {
                    if (current.Length > 0)
                    {
                        var line = current.ToString();
                        fullOutput.AppendLine(line);
                        onLine(line);
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (current.Length > 0)
        {
            var line = current.ToString();
            fullOutput.AppendLine(line);
            onLine(line);
        }

        return fullOutput.ToString();
    }
}
