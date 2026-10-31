using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Shutdown_Scheduler.Services;

public sealed class WinUiUserNotificationService : IUserNotificationService
{
    private readonly Func<XamlRoot?> _xamlRootProvider;
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUiUserNotificationService(Func<XamlRoot?> xamlRootProvider, DispatcherQueue dispatcherQueue)
    {
        _xamlRootProvider = xamlRootProvider;
        _dispatcherQueue = dispatcherQueue;
    }

    public void ShowWarning(string title, string message)
    {
        _dispatcherQueue.TryEnqueue(async () =>
        {
            var root = _xamlRootProvider();
            if (root is null) return;

            // WinUI allows one ContentDialog at a time; wait for any dialog already on screen instead of failing.
            while (VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0)
            {
                await Task.Delay(500);
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = root
            };
            ThemeResources.ApplyDialogStyle(dialog);

            await dialog.ShowAsync();
        });
    }
}
