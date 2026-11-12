using System;
using System.IO;
using System.Runtime.Versioning;
using BorgMate.Infrastructure.Platform;
using Velopack.Locators;
using Velopack.Logging;

namespace BorgMate.Infrastructure.Updates;

/// <summary>
///     Keeps downloaded update packages in the user's private cache. The stock Linux locator uses the
///     shared <c>/var/tmp/velopack</c>, where another local user can plant or swap a package between
///     its verification and its installation.
/// </summary>
[SupportedOSPlatform("linux")]
public class UserCacheLinuxVelopackLocator : LinuxVelopackLocator
{
    public UserCacheLinuxVelopackLocator(IVelopackLogger logger)
        : base(new DefaultProcessImpl(logger), logger)
    {
    }

    public override string? PackagesDir => AppId is null ? null : PrivateDirectory.Ensure(PackagesPath(AppId));

    private static string PackagesPath(string appId)
    {
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrEmpty(cacheHome) || !Path.IsPathRooted(cacheHome))
            cacheHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(cacheHome, "velopack", appId, "packages");
    }
}
