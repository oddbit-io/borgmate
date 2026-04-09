namespace BorgMate.Services.AutoStart;

/// <summary>
/// Manages start-at-login via platform-specific mechanisms
/// (macOS LaunchAgent, Windows Registry, Linux XDG autostart).
/// </summary>
public interface IAutoStartService
{
    void SetEnabled(bool enabled);
    bool IsEnabled();
}

internal class NoOpAutoStartService : IAutoStartService
{
    public void SetEnabled(bool enabled) { }
    public bool IsEnabled() => false;
}
