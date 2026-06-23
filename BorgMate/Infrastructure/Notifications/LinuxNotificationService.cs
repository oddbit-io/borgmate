using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Platform;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace BorgMate.Infrastructure.Notifications;

public class LinuxNotificationService : INotificationService
{
    private const string NotificationBusName = "org.freedesktop.Notifications";
    private const string NotificationObjectPath = "/org/freedesktop/Notifications";
    private const string NotificationInterface = "org.freedesktop.Notifications";
    private const string NotifyMethod = "Notify";
    private const string NotifySignature = "susssasa{sv}i";
    private static readonly object ReaderState = new();
    private readonly ILogger<LinuxNotificationService> _logger;

    public LinuxNotificationService(ILogger<LinuxNotificationService> logger)
    {
        _logger = logger;
    }

    public void Send(string title, string body)
    {
        _ = SendAsync(title, body);
    }

    private async Task SendAsync(string title, string body)
    {
        try
        {
            var connection = DBusConnection.Session;
            var message = CreateNotificationMessage(connection, title, body);
            await connection.CallMethodAsync(message, ReadNotificationId, ReaderState);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Linux notification through D-Bus");
        }
    }

    private static MessageBuffer CreateNotificationMessage(DBusConnection connection, string title, string body)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            NotificationBusName,
            NotificationObjectPath,
            NotificationInterface,
            NotifyMethod,
            NotifySignature,
            MessageFlags.None);
        writer.WriteString(StringHelpers.AppName);
        writer.WriteUInt32(0);
        writer.WriteString("");
        writer.WriteString(title);
        writer.WriteString(body);
        writer.WriteArray(Array.Empty<string>());
        writer.WriteDictionary(Array.Empty<KeyValuePair<string, VariantValue>>());
        writer.WriteInt32(-1);

        return writer.CreateMessage();
    }

    private static uint ReadNotificationId(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return reader.ReadUInt32();
    }
}
