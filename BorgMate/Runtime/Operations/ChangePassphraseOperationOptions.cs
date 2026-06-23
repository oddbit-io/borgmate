using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for changing a repository encryption passphrase.</summary>
public sealed record ChangePassphraseOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string NewPassphrase,
    bool SaveToCredentialStore) : BorgOperationOptions(Repository, ProgressSink);