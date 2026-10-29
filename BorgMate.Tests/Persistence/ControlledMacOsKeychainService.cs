using BorgMate.Persistence.Keychain;
using Microsoft.Extensions.Logging.Abstractions;

namespace BorgMate.Tests.Persistence;

public class ControlledMacOsKeychainService : MacOsKeychainService
{
    public ControlledMacOsKeychainService() : base(NullLogger<MacOsKeychainService>.Instance)
    {
    }

    public bool InteractionAllowed { get; set; } = true;
    public Func<string?> Read { get; set; } = () => "secret";

    protected override bool GetUserInteractionAllowed() => InteractionAllowed;

    protected override void SetUserInteractionAllowed(bool allowed) => InteractionAllowed = allowed;

    protected override string? FindPassphrase(string account) => Read();
}
