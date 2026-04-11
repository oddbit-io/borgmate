using System;
using BorgMate.Models;
using BorgMate.Services.Config;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Notifications;

/// <summary>
/// Sends native OS notifications for completed operations.
/// Delegates to platform-specific INotificationService.
/// </summary>
public class NotificationService
{
    private readonly INotificationService? _platform;
    private readonly AppSettings _settings;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(INotificationService platform, AppSettings settings, ILogger<NotificationService> logger)
    {
        _platform = platform;
        _settings = settings;
        _logger = logger;
    }

    public void NotifyCompleted(JournalEntry entry)
    {
        if (!_settings.ShowNotifications) return;
        if (entry.Result == JournalResult.Running) return;

        var title = Localization.Strings.FormatJournalTitle(entry.EventKind, entry.TitleArgs);
        var body = Localization.Strings.FormatJournalResult(entry.Result);

        try
        {
            _logger.LogInformation("Sending notification: {Title}", title);
            _platform?.Send(title, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send OS notification");
        }
    }
}
