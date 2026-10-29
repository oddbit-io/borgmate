using System.Threading.Tasks;

namespace BorgMate.Persistence.Keychain;

/// <summary>
///     Platform-specific passphrase storage. Keys are repo paths for borg passphrases
///     or "sshkey:{path}" for SSH key passphrases.
/// </summary>
public interface ICredentialStore
{
    /// <param name="allowInteraction">On macOS, permits system keychain authorization dialogs.</param>
    Task<string?> GetPassphraseAsync(string repoPath, bool allowInteraction = true);
    Task<bool> SetPassphraseAsync(string repoPath, string passphrase);
    Task DeletePassphraseAsync(string repoPath);
}
