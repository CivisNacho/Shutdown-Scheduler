namespace Shutdown_Scheduler.Services;

public interface IUserNotificationService
{
    void ShowWarning(string title, string message);
}

