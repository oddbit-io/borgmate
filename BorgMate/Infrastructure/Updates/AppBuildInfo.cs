using System;
using System.Reflection;

namespace BorgMate.Infrastructure.Updates;

internal class AppBuildInfo
{
    public AppBuildInfo(string displayVersion)
    {
        if (!SemVerParser.TryParse(displayVersion, out var version) || version is null)
            throw new InvalidOperationException($"Invalid BorgMate build version: {displayVersion}");

        DisplayVersion = displayVersion;
        CurrentVersion = version.ToNormalizedString();
    }

    public string DisplayVersion { get; }
    public string CurrentVersion { get; }

    public static AppBuildInfo FromAssembly(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? throw new InvalidOperationException("BorgMate assembly has no informational version.");
        return new AppBuildInfo(version);
    }
}
