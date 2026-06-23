namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Manages start-at-login via platform-specific mechanisms
///     (macOS LaunchAgent, Windows Registry, Linux XDG autostart).
/// </summary>
public interface IAutoStartService
{
    void SetEnabled(bool enabled);
    bool IsEnabled();
}