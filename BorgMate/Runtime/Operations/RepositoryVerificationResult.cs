using BorgMate.Domain.Repositories;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful repository verification result.</summary>
public sealed record RepositoryVerificationResult(BorgRepository Repository, bool WasInitialized);