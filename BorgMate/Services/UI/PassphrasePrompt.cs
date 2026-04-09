using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using BorgMate.Models;
using BorgMate.Services.Keychain;

namespace BorgMate.Services.UI;

public class PassphrasePrompt(IKeychainService? keychain)
{
    /// <summary>
    /// Ensures a passphrase is available for the repo, checking keychain then prompting the user.
    /// </summary>
    public virtual async Task<bool> EnsurePassphraseAsync(BorgRepository repo)
    {
        if (repo.EncryptionMode == BorgEncryptionMode.None)
            return true;

        // If passphrase was previously rejected, force a new prompt
        if (repo.WrongPassphrase)
        {
            var ok = await PromptAndStoreAsync(
                repo.Name, repo.Path,
                passphrase => repo.Passphrase = passphrase,
                message: Strings.Get("WrongPassphraseRetry"));
            if (ok) repo.WrongPassphrase = false;
            return ok;
        }

        if (!string.IsNullOrWhiteSpace(repo.Passphrase))
            return true;

        return await PromptAndStoreAsync(
            repo.Name, repo.Path,
            passphrase => repo.Passphrase = passphrase,
            tryKeychain: true);
    }

    /// <summary>
    /// Ensures a passphrase for init (before repo model is fully set up).
    /// </summary>
    public virtual Task<bool> EnsurePassphraseAsync(
        BorgEncryptionMode encryption, string repoName, string repoPath, Action<string> setPassphrase)
    {
        if (encryption == BorgEncryptionMode.None)
            return Task.FromResult(true);

        return PromptAndStoreAsync(repoName, repoPath, setPassphrase);
    }

    /// <summary>
    /// Re-prompts after a wrong passphrase error. Clears the old keychain entry first.
    /// </summary>
    public virtual async Task<bool> RePromptAsync(BorgRepository repo)
    {
        if (keychain is not null)
            await keychain.DeletePassphraseAsync(repo.Path);

        repo.Passphrase = string.Empty;

        return await PromptAndStoreAsync(
            repo.Name, repo.Path,
            passphrase => repo.Passphrase = passphrase,
            message: Strings.Get("WrongPassphraseRetry"));
    }

    private async Task<bool> PromptAndStoreAsync(
        string repoName, string repoPath, Action<string> setPassphrase,
        bool tryKeychain = false, string? message = null)
    {
        if (tryKeychain && keychain is not null)
        {
            var stored = await keychain.GetPassphraseAsync(repoPath);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                setPassphrase(stored);
                return true;
            }
        }

        var (passphrase, saveToKeychain) = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = DialogHelper.GetMainWindow();
            if (window is null) return (null, false);
            return await ShowPassphraseDialog(window, repoName, message);
        });
        if (passphrase is null) return false;

        setPassphrase(passphrase);

        if (saveToKeychain && keychain is not null)
            await keychain.SetPassphraseAsync(repoPath, passphrase);

        return true;
    }

    private static Task<(string? password, bool saveToKeychain)> ShowPassphraseDialog(
        Window parent, string repoName, string? message = null) =>
        DialogHelper.ShowPasswordDialogAsync(parent,
            Strings.Get("PassphraseRequired"),
            message ?? string.Format(Strings.Get("EnterPassphraseFor"), repoName),
            Strings.Get("WatermarkPassphrase"));
}
