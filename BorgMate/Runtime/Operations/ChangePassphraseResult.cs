using System;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful passphrase change operation result.</summary>
public sealed record ChangePassphraseResult(DateTimeOffset CompletedAt, bool SavedToCredentialStore);