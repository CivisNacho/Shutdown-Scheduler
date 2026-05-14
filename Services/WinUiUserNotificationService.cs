using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = root
            };

            await dialog.ShowAsync();
        });
    }
}
