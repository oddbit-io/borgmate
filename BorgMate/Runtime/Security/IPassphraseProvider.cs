using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using OneOf;

namespace BorgMate.Runtime.Security;

/// <summary>
///     Provides in-memory repository passphrases and wrong-passphrase state keyed by repository path.
/// </summary>
public interface IPassphraseProvider
{
    /// <summary>Ensures a passphrase is available for <paramref name="repo" />.</summary>
    Task<OneOf<PassphraseResult, OperationError>> EnsureAsync(
        BorgRepository repo, CancellationToken ct = default);

    /// <summary>Clears any cached passphrase and asks the user for a replacement.</summary>
    Task<OneOf<PassphraseResult, OperationError>> RePromptAsync(
        BorgRepository repo, CancellationToken ct = default);

    /// <summary>Clears the cached passphrase for <paramref name="repo" />.</summary>
    void Clear(BorgRepository repo);

    /// <summary>Marks <paramref name="repo" /> as having failed passphrase authentication.</summary>
    void MarkWrong(BorgRepository repo);

    /// <summary>Returns true when <paramref name="repo" /> should force a new passphrase prompt.</summary>
    bool IsWrong(BorgRepository repo);
}