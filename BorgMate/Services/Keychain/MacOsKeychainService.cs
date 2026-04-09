using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Keychain;

public class MacOsKeychainService(ILogger<MacOsKeychainService> logger)
    : KeychainServiceBase(logger), IKeychainService
{
    public async Task<string?> GetPassphraseAsync(string repoPath)
    {
        var account = SanitizeAccount(repoPath);
        try
        {
            var result = await RunAsync("security",
                $"find-generic-password -s \"{ServiceName}\" -a \"{account}\" -w");
            return result.ExitCode == 0 ? result.Output.Trim() : null;
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
            await RunAsync("security",
                $"delete-generic-password -s \"{ServiceName}\" -a \"{account}\"");
            var result = await RunAsync("security",
                $"add-generic-password -s \"{ServiceName}\" -a \"{account}\" -w \"{EscapeShell(passphrase)}\"");
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
            await RunAsync("security",
                $"delete-generic-password -s \"{ServiceName}\" -a \"{account}\"");
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to delete passphrase from keychain for {Account}", account);
        }
    }
}
