using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Platform;
using Microsoft.Extensions.Logging;

namespace BorgMate.Persistence.Keychain;

/// <summary>
///     Stores passphrases as generic passwords through the Security framework, so a secret never
///     appears in a process command line. Items written earlier by the <c>security</c> CLI trust only
///     that tool, so macOS asks once per item before BorgMate may read or update it. The app binary is
///     not code-signed, so the keychain grants access to one exact build and asks again after an update.
/// </summary>
public class MacOsKeychainService(ILogger<MacOsKeychainService> logger)
    : KeychainServiceBase(logger), ICredentialStore
{
    private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";

    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;

    // The file-based keychain's interaction flag is process-wide. Every call in this store shares the gate.
    private static readonly SemaphoreSlim InteractionGate = new(1, 1);

    // SecItem calls block while macOS shows a keychain access prompt, so they run off the caller's thread.
    public async Task<string?> GetPassphraseAsync(string repoPath, bool allowInteraction = true)
    {
        if (!await InteractionGate.WaitAsync(allowInteraction ? Timeout.Infinite : 0).ConfigureAwait(false))
        {
            Logger.LogWarning("Keychain access requires waiting for an interactive operation");
            return null;
        }

        try
        {
            return await Task.Run(() =>
            {
                var previous = GetUserInteractionAllowed();
                SetUserInteractionAllowed(allowInteraction);
                try
                {
                    return FindPassphrase(SanitizeAccount(repoPath));
                }
                finally
                {
                    SetUserInteractionAllowed(previous);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            InteractionGate.Release();
        }
    }

    public async Task<bool> SetPassphraseAsync(string repoPath, string passphrase)
    {
        await InteractionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => StorePassphrase(SanitizeAccount(repoPath), passphrase)).ConfigureAwait(false);
        }
        finally
        {
            InteractionGate.Release();
        }
    }

    public async Task DeletePassphraseAsync(string repoPath)
    {
        await InteractionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => DeletePassphrase(SanitizeAccount(repoPath))).ConfigureAwait(false);
        }
        finally
        {
            InteractionGate.Release();
        }
    }

    protected virtual string? FindPassphrase(string account)
    {
        using var cf = new CoreFoundationScope();
        var query = CreateItemQuery(cf, account);
        CoreFoundationNative.CFDictionarySetValue(query, SecConstant("kSecReturnData"), CoreFoundationNative.BooleanTrue);
        CoreFoundationNative.CFDictionarySetValue(query, SecConstant("kSecMatchLimit"), SecConstant("kSecMatchLimitOne"));

        var status = SecItemCopyMatching(query, out var data);
        if (status == ErrSecItemNotFound)
            return null;
        if (status != ErrSecSuccess)
        {
            Logger.LogWarning("Failed to retrieve passphrase from keychain for {Account}: OSStatus {Status}",
                account, status);
            return null;
        }

        return Encoding.UTF8.GetString(CoreFoundationNative.GetBytes(cf.Own(data)));
    }

    private bool StorePassphrase(string account, string passphrase)
    {
        using var cf = new CoreFoundationScope();
        var secret = cf.Own(CoreFoundationNative.CreateData(Encoding.UTF8.GetBytes(passphrase)));
        var query = CreateItemQuery(cf, account);
        var changes = cf.Own(CoreFoundationNative.CreateMutableDictionary());
        CoreFoundationNative.CFDictionarySetValue(changes, SecConstant("kSecValueData"), secret);

        var status = SecItemUpdate(query, changes);
        if (status == ErrSecItemNotFound)
        {
            CoreFoundationNative.CFDictionarySetValue(query, SecConstant("kSecValueData"), secret);
            status = SecItemAdd(query, IntPtr.Zero);
        }

        if (status != ErrSecSuccess)
            Logger.LogWarning("Failed to store passphrase in keychain for {Account}: OSStatus {Status}",
                account, status);
        return status == ErrSecSuccess;
    }

    private void DeletePassphrase(string account)
    {
        using var cf = new CoreFoundationScope();
        var status = SecItemDelete(CreateItemQuery(cf, account));
        if (status is not (ErrSecSuccess or ErrSecItemNotFound))
            Logger.LogDebug("Failed to delete passphrase from keychain for {Account}: OSStatus {Status}",
                account, status);
    }

    private static IntPtr CreateItemQuery(CoreFoundationScope cf, string account)
    {
        var query = cf.Own(CoreFoundationNative.CreateMutableDictionary());
        CoreFoundationNative.CFDictionarySetValue(query, SecConstant("kSecClass"),
            SecConstant("kSecClassGenericPassword"));
        CoreFoundationNative.CFDictionarySetValue(query, SecConstant("kSecAttrService"),
            cf.Own(CoreFoundationNative.CreateString(ServiceName)));
        CoreFoundationNative.CFDictionarySetValue(query, SecConstant("kSecAttrAccount"),
            cf.Own(CoreFoundationNative.CreateString(account)));
        return query;
    }

    private static IntPtr SecConstant(string name)
    {
        return CoreFoundationNative.ReadConstant(NativeLibrary.Load(SecurityLib), name);
    }

    protected virtual bool GetUserInteractionAllowed()
    {
        var status = SecKeychainGetUserInteractionAllowed(out var allowed);
        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"Cannot read keychain interaction policy: OSStatus {status}.");
        return allowed;
    }

    protected virtual void SetUserInteractionAllowed(bool allowed)
    {
        var status = SecKeychainSetUserInteractionAllowed(allowed);
        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"Cannot set keychain interaction policy: OSStatus {status}.");
    }

    [DllImport(SecurityLib)]
    private static extern int SecKeychainGetUserInteractionAllowed([MarshalAs(UnmanagedType.I1)] out bool allowed);

    [DllImport(SecurityLib)]
    private static extern int SecKeychainSetUserInteractionAllowed([MarshalAs(UnmanagedType.I1)] bool allowed);

    [DllImport(SecurityLib)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityLib)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityLib)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(SecurityLib)]
    private static extern int SecItemDelete(IntPtr query);
}
