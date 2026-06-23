using System.Threading.Tasks;

namespace BorgMate.Persistence.Keychain;

/// <summary>
///     Platform-specific passphrase storage. Keys are repo paths for borg passphrases
///     or "sshkey:{path}" for SSH key passphrases.
/// </summary>
public interface ICredentialStore
{
    Task<string?> GetPassphraseAsync(string repoPath);
    Task<bool> SetPassphraseAsync(string repoPath, string passphrase);
    Task DeletePassphraseAsync(string repoPath);
}