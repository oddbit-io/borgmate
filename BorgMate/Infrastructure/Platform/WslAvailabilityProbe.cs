using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BorgMate.Infrastructure.Platform;

internal class WslAvailabilityProbe : IWslAvailabilityProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly ILogger _logger;

    public WslAvailabilityProbe(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        Process? process = null;
        using var timeoutCts = new CancellationTokenSource(ProbeTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = "--list --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process = Process.Start(psi);
            if (process is null)
                return false;

            var outputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);
            var output = await outputTask;
            await errorTask;

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            _logger.LogWarning("WSL availability check timed out after {TimeoutSeconds} seconds", ProbeTimeout.TotalSeconds);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "WSL executable was not found");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WSL availability check failed");
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void TryKill(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
            /* already exited */
        }
    }
}
