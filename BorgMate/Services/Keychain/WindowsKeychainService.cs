using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Keychain;

[SupportedOSPlatform("windows")]
public class WindowsKeychainService(ILogger<WindowsKeychainService> logger)
    : KeychainServiceBase(logger), IKeychainService
{
    public Task<string?> GetPassphraseAsync(string repoPath)
    {
        var target = $"BorgMate:{SanitizeAccount(repoPath)}";
        try
        {
            if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credPtr))
                return Task.FromResult<string?>(null);

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                {
                    var passphrase = Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2);
                    return Task.FromResult<string?>(passphrase);
                }
                return Task.FromResult<string?>(null);
            }
            finally
            {
                CredFree(credPtr);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve passphrase from credential store");
            return Task.FromResult<string?>(null);
        }
    }

    public Task<bool> SetPassphraseAsync(string repoPath, string passphrase)
    {
        var target = $"BorgMate:{SanitizeAccount(repoPath)}";
        try
        {
            var bytes = Encoding.Unicode.GetBytes(passphrase);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = Marshal.AllocHGlobal(bytes.Length),
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = "BorgMate"
            };

            try
            {
                Marshal.Copy(bytes, 0, cred.CredentialBlob, bytes.Length);
                var result = CredWrite(ref cred, 0);
                return Task.FromResult(result);
            }
            finally
            {
                Marshal.FreeHGlobal(cred.CredentialBlob);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to store passphrase in credential store");
            return Task.FromResult(false);
        }
    }

    public Task DeletePassphraseAsync(string repoPath)
    {
        var target = $"BorgMate:{SanitizeAccount(repoPath)}";
        try
        {
            CredDelete(target, CRED_TYPE_GENERIC, 0);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to delete passphrase from credential store");
        }
        return Task.CompletedTask;
    }

    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
