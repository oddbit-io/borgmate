using System;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Runtime.Operations;

/// <summary>Exposes the version and features of the configured Borg 1 executable at startup.</summary>
public class BorgCapabilityQuery
{
    public BorgCapabilityQuery(AppSettings settings)
        : this(settings, BorgPathHelper.TryReadCliVersion)
    {
    }

    internal BorgCapabilityQuery(AppSettings settings, Func<string, BorgCliVersion?> versionProbe)
    {
        Borg1Version = string.IsNullOrWhiteSpace(settings.Borg1BinaryPath)
            ? settings.DetectedBorg1Version
            : versionProbe(settings.Borg1BinaryPath);
    }

    public BorgCliVersion? Borg1Version { get; }

    public bool SupportsExcludeDataless => Borg1Version?.Supports(BorgCliFeature.ExcludeDataless) == true;
}
