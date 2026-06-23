using System;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace BorgMate.Infrastructure.Notifications;

/// <summary>
///     Sends native OS notifications for completed operations.
///     Delegates to platform-specific INotificationService.
/// </summary>
public class NotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly INotificationService? _platform;
    private readonly AppSettings _settings;

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

        var title = Strings.FormatJournalTitle(entry.EventKind, entry.TitleArgs);
        var body = Strings.FormatJournalResult(entry.Result);

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