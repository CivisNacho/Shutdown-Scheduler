using System.Windows;

namespace Shutdown_Scheduler.Services;

public sealed class WpfUserNotificationService : IUserNotificationService
{
    public void ShowWarning(string title, string message)
        => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
