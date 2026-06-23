using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using BorgMate.Domain.Repositories;
using BorgMate.Persistence.Keychain;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using OneOf;

namespace BorgMate.Runtime.Security;

public class PromptingPassphraseProvider(ICredentialStore? keychain) : IPassphraseProvider
{
    private readonly ConcurrentDictionary<string, string> _passphrases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _wrongPassphrases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<OneOf<PassphraseResult, OperationError>> EnsureAsync(
        BorgRepository repo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await EnsurePassphraseAsync(repo)
            ? new PassphraseResult(GetCachedPassphrase(repo) ?? string.Empty)
            : new OperationError.Cancelled();
    }

    /// <inheritdoc />
    public async Task<OneOf<PassphraseResult, OperationError>> RePromptAsync(
        BorgRepository repo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await RePromptAsync(repo)
            ? new PassphraseResult(GetCachedPassphrase(repo) ?? string.Empty)
            : new OperationError.Cancelled();
    }

    /// <inheritdoc />
    public void Clear(BorgRepository repo)
    {
        _passphrases.TryRemove(repo.Path, out _);
    }

    /// <inheritdoc />
    public void MarkWrong(BorgRepository repo)
    {
        _wrongPassphrases[repo.Path] = 0;
    }

    /// <inheritdoc />
    public bool IsWrong(BorgRepository repo)
    {
        return _wrongPassphrases.ContainsKey(repo.Path);
    }

    /// <summary>Returns the cached passphrase for <paramref name="repo" />, if one is available.</summary>
    public string? GetCachedPassphrase(BorgRepository repo)
    {
        return _passphrases.TryGetValue(repo.Path, out var passphrase) ? passphrase : null;
    }

    /// <summary>Caches <paramref name="passphrase" /> for <paramref name="repo" />.</summary>
    public void Set(BorgRepository repo, string passphrase)
    {
        _passphrases[repo.Path] = passphrase;
        _wrongPassphrases.TryRemove(repo.Path, out _);
    }

    /// <summary>
    ///     Ensures a passphrase is available for the repo, checking keychain then prompting the user.
    /// </summary>
    public virtual async Task<bool> EnsurePassphraseAsync(BorgRepository repo)
    {
        if (!repo.IsEncrypted)
            return true;

        // If passphrase was previously rejected, force a new prompt
        if (IsWrong(repo))
        {
            var ok = await PromptAndStoreAsync(
                repo.Name, repo.Path,
                passphrase => Set(repo, passphrase),
                message: Strings.Get("WrongPassphraseRetry"));
            return ok;
        }

        if (!string.IsNullOrWhiteSpace(GetCachedPassphrase(repo)))
            return true;

        return await PromptAndStoreAsync(
            repo.Name, repo.Path,
            passphrase => Set(repo, passphrase),
            true);
    }

    /// <summary>
    ///     Ensures a passphrase for init (before repo model is fully set up).
    /// </summary>
    public virtual Task<bool> EnsurePassphraseAsync(
        BorgEncryptionMode encryption, string repoName, string repoPath, Action<string> setPassphrase)
    {
        if (encryption == BorgEncryptionMode.None)
            return Task.FromResult(true);

        return PromptAndStoreAsync(repoName, repoPath, passphrase =>
        {
            _passphrases[repoPath] = passphrase;
            _wrongPassphrases.TryRemove(repoPath, out _);
            setPassphrase(passphrase);
        });
    }

    /// <summary>
    ///     Re-prompts after a wrong passphrase error. Clears the old keychain entry first.
    /// </summary>
    public virtual async Task<bool> RePromptAsync(BorgRepository repo)
    {
        if (keychain is not null)
            await keychain.DeletePassphraseAsync(repo.Path);

        Clear(repo);

        return await PromptAndStoreAsync(
            repo.Name, repo.Path,
            passphrase => Set(repo, passphrase),
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
        Window parent, string repoName, string? message = null)
    {
        return DialogHelper.ShowPasswordDialogAsync(parent,
            Strings.Get("PassphraseRequired"),
            message ?? string.Format(Strings.Get("EnterPassphraseFor"), repoName),
            Strings.Get("WatermarkPassphrase"));
    }
}
