using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Keychain;

public class LinuxKeychainService(ILogger<LinuxKeychainService> logger)
    : KeychainServiceBase(logger), IKeychainService
{
    public async Task<string?> GetPassphraseAsync(string repoPath)
    {
        var account = SanitizeAccount(repoPath);
        try
        {
            var result = await RunAsync("secret-tool",
                $"lookup service \"{ServiceName}\" account \"{account}\"");
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output)
                ? result.Output.Trim()
                : null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve passphrase from keychain for {Account}", account);
            return null;
        }
    }

    public async Task<bool> SetPassphraseAsync(string repoPath, string passphrase)
    {
        var account = SanitizeAccount(repoPath);
        try
        {
            var result = await RunAsync("secret-tool",
                $"store --label=\"BorgMate: {account}\" service \"{ServiceName}\" account \"{account}\"",
                stdinData: passphrase);
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to store passphrase in keychain for {Account}", account);
            return false;
        }
    }

    public async Task DeletePassphraseAsync(string repoPath)
    {
        var account = SanitizeAccount(repoPath);
        try
        {
            await RunAsync("secret-tool",
                $"clear service \"{ServiceName}\" account \"{account}\"");
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to delete passphrase from keychain for {Account}", account);
        }
    }
}
