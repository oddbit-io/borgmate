using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Infrastructure.Borg;

public class BorgServiceFactory
{
    private readonly ILoggerFactory loggerFactory;
    private readonly AppSettings settings;
    private readonly SshRepositoryTransport sshTransport;
    private readonly WslHelper wsl;
    private readonly INetworkShareMounter shareMounter;
    private readonly PromptingPassphraseProvider? passphrase;
    private Borg1Service? _borg1;
    private Borg2Service? _borg2;

    public BorgServiceFactory(
        ILoggerFactory loggerFactory,
        AppSettings settings,
        SshRepositoryTransport sshTransport,
        WslHelper wsl,
        INetworkShareMounter shareMounter,
        PromptingPassphraseProvider? passphrase = null)
    {
        this.loggerFactory = loggerFactory;
        this.settings = settings;
        this.sshTransport = sshTransport;
        this.wsl = wsl;
        this.shareMounter = shareMounter;
        this.passphrase = passphrase;
    }

    public virtual IBorgService GetService(BorgVersion borgVersion)
    {
        return borgVersion == BorgVersion.Borg2
            ? _borg2 ??= new Borg2Service(loggerFactory.CreateLogger<Borg2Service>(), settings, sshTransport, wsl,
                shareMounter, passphrase)
            : _borg1 ??= new Borg1Service(loggerFactory.CreateLogger<Borg1Service>(), settings, sshTransport, wsl,
                shareMounter, passphrase);
    }
}
