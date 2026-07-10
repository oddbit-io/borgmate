using BorgMate.Persistence.Keychain;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests.Persistence;

public class KeychainServiceBaseTests
{
    private sealed class TestKeychainService(ILogger logger) : KeychainServiceBase(logger)
    {
        public async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
            string fileName, params string[] arguments)
        {
            var result = await RunAsync(fileName, arguments);
            return (result.ExitCode, result.Output, result.Error);
        }
    }

    [Fact]
    public async Task RunAsync_CapturesStandardError_OnFailure()
    {
        if (OperatingSystem.IsWindows())
            return;

        var service = new TestKeychainService(Substitute.For<ILogger>());
        var result = await service.RunProcessAsync("/bin/sh", "-c", "echo ring is not available >&2; exit 1");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ring is not available", result.Error);
    }
}
