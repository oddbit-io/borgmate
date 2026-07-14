using System;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Base class for Borg 2 services and helpers.</summary>
public abstract class Borg2ServiceBase : BorgServiceBase
{
    protected Borg2ServiceBase(
        ILogger logger,
        AppSettings settings,
        SshRepositoryTransport sshTransport,
        WslHelper wsl,
        INetworkShareMounter shareMounter,
        PromptingPassphraseProvider passphrase)
        : base(logger, settings, BorgVersion.Borg2, sshTransport, wsl, shareMounter, passphrase)
    {
    }

    /// <summary>Adds the Borg 2 repository environment variable used by b21 commands.</summary>
    protected void AddRepositoryEnvironment(BorgRepository repo, BorgEnv env)
    {
        env.Variables["BORG_REPO"] = BuildRepositoryLocation(repo);
    }

    /// <summary>Formats the configured repository location for Borg 2's <c>BORG_REPO</c>.</summary>
    protected string BuildRepositoryLocation(BorgRepository repo)
    {
        if (repo.LocationKind == BorgRepositoryLocationKind.BorgUrl)
            return !string.IsNullOrWhiteSpace(repo.BorgUrl) ? repo.BorgUrl : repo.Path;

        if (!repo.IsSsh)
            return Location(repo);

        var target = SshTransport.ResolveTarget(repo);
        var port = repo.SshPort is > 0 and not 22 ? $":{repo.SshPort}" : string.Empty;
        var pathPart = target.RepositoryPath.StartsWith("/", StringComparison.Ordinal)
            ? $"//{target.RepositoryPath.TrimStart('/')}"
            : $"/{target.RepositoryPath}";
        return $"ssh://{target.Host}{port}{pathPart}";
    }
}
