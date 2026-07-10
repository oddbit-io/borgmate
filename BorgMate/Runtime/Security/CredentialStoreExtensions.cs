using System;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Keychain;

namespace BorgMate.Runtime.Security;

/// <summary>Keychain writes with user-visible feedback when the platform store rejects them.</summary>
public static class CredentialStoreExtensions
{
    /// <summary>
    ///     Stores a passphrase and notifies the user when the keychain is unavailable
    ///     (e.g. no running Secret Service on Linux), instead of failing silently.
    /// </summary>
    public static async Task<bool> TrySetPassphraseAsync(
        this ICredentialStore store, INotificationService? notifier, string repoPath, string passphrase)
    {
        if (await store.SetPassphraseAsync(repoPath, passphrase))
            return true;

        var body = Strings.Get("Error.KeychainSaveFailed");
        if (OperatingSystem.IsLinux())
            body = $"{body} {Strings.Get("Error.KeychainSaveFailedLinuxHint")}";

        try
        {
            notifier?.Send(StringHelpers.AppName, body);
        }
        catch (Exception)
        {
            // Notification delivery is best-effort; the keychain service already logged the cause.
        }

        return false;
    }
}
