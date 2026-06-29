using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Infrastructure.Platform;

internal class MacOsPowerSource : IPowerSource
{
    public async Task<bool> IsOnBatteryAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await ProcessRunner.RunAsync("/usr/bin/pmset", ["-g", "batt"], cts.Token);
            return result.Success &&
                   result.StandardOutput.Contains("'Battery Power'", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
