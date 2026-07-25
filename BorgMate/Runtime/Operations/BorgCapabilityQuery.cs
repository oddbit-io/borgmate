using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Runtime.Operations;

/// <summary>Exposes optional features discovered while probing the Borg executable path.</summary>
public class BorgCapabilityQuery
{
    public BorgCapabilityQuery(AppSettings settings)
    {
        SupportsExcludeDataless =
            settings.DetectedBorg1Version?.Supports(BorgCliFeature.ExcludeDataless) == true;
    }

    public bool SupportsExcludeDataless { get; }
}
