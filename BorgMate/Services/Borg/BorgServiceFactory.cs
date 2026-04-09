using BorgMate.Models;
using BorgMate.Services.Config;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Borg;

public class BorgServiceFactory(ILoggerFactory loggerFactory, AppSettings settings, SshAgentHelper sshAgent, WslHelper wsl)
{
    private Borg1Service? _borg1;
    private Borg2Service? _borg2;

    public IBorgService GetService(BorgVersion borgVersion)
    {
        return borgVersion == BorgVersion.Borg2
            ? _borg2 ??= new Borg2Service(loggerFactory.CreateLogger<Borg2Service>(), settings, sshAgent, wsl)
            : _borg1 ??= new Borg1Service(loggerFactory.CreateLogger<Borg1Service>(), settings, sshAgent, wsl);
    }
}
