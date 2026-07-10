using System;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Base class for Borg 2 services and helpers.</summary>
public abstract class Borg2ServiceBase(
    ILogger logger,
    AppSettings settings,
    SshAgentHelper sshAgent,
    WslHelper wsl,
    INetworkShareMounter shareMounter,
    PromptingPassphraseProvider passphrase)
    : BorgServiceBase(logger, settings, BorgVersion.Borg2, sshAgent, wsl, shareMounter, passphrase)
{
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

        var (host, path) = SplitLegacySshPath(repo.Path);
        var port = repo.SshPort is > 0 and not 22 ? $":{repo.SshPort}" : string.Empty;
        var pathPart = path.StartsWith("/", StringComparison.Ordinal)
            ? $"//{path.TrimStart('/')}"
            : $"/{path}";
        return $"ssh://{host}{port}{pathPart}";
    }

    /// <inheritdoc />
    public override bool UsesSshTransport(BorgRepository repo)
    {
        return repo.IsSsh ||
               repo.LocationKind == BorgRepositoryLocationKind.BorgUrl &&
               BorgRepositoryUrlValidator.UsesSshTransport(
                   !string.IsNullOrWhiteSpace(repo.BorgUrl) ? repo.BorgUrl : repo.Path);
    }

    private static (string Host, string Path) SplitLegacySshPath(string path)
    {
        var colonIndex = path.IndexOf(':', StringComparison.Ordinal);
        return colonIndex < 0
            ? (path, string.Empty)
            : (path[..colonIndex], path[(colonIndex + 1)..]);
    }
}
