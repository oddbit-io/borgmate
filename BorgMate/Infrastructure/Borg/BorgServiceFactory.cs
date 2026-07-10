using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Infrastructure.Borg;

public class BorgServiceFactory(
    ILoggerFactory loggerFactory,
    AppSettings settings,
    SshAgentHelper sshAgent,
    WslHelper wsl,
    INetworkShareMounter shareMounter,
    PromptingPassphraseProvider? passphrase = null)
{
    private Borg1Service? _borg1;
    private Borg2Service? _borg2;

    public virtual IBorgService GetService(BorgVersion borgVersion)
    {
        return borgVersion == BorgVersion.Borg2
            ? _borg2 ??= new Borg2Service(loggerFactory.CreateLogger<Borg2Service>(), settings, sshAgent, wsl,
                shareMounter, passphrase)
            : _borg1 ??= new Borg1Service(loggerFactory.CreateLogger<Borg1Service>(), settings, sshAgent, wsl,
                shareMounter, passphrase);
    }
}