namespace BorgMate.Services.Notifications;

/// <summary>Sends OS-level notifications (not in-app). Called on operation completion.</summary>
public interface INotificationService
{
    void Send(string title, string body);
}
