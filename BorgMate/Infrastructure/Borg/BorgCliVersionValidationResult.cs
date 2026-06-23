namespace BorgMate.Infrastructure.Borg;

/// <summary>Result of validating a selected borg binary against a service-specific CLI policy.</summary>
public sealed record BorgCliVersionValidationResult(
    BorgCliVersion? Version,
    string? ValidationMessageKey = null,
    BorgResult? FailureResult = null)
{
    /// <summary>True when the selected binary passed the service-specific validation policy.</summary>
    public bool IsValid => Version is not null && ValidationMessageKey is null && FailureResult is null;

    /// <summary>Creates a validation failure caused by an unsupported or mismatched version.</summary>
    public static BorgCliVersionValidationResult Validation(string messageKey, BorgCliVersion? version = null)
    {
        return new BorgCliVersionValidationResult(version, messageKey);
    }

    /// <summary>Creates a validation failure caused by the borg process itself failing.</summary>
    public static BorgCliVersionValidationResult Failure(BorgResult result)
    {
        return new BorgCliVersionValidationResult(null, FailureResult: result);
    }
}
