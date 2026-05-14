using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Shutdown_Scheduler.Models;
using Windows.Graphics;
using WinRT.Interop;

namespace Shutdown_Scheduler_WinUI.Views;

public sealed partial class WidgetWindow : Window
{
    private AppWindow? _appWindow;

    public WidgetWindow()
    {
        InitializeComponent();
        ConfigureAppWindow();
    }

    public void ApplySettings(WidgetSettings settings)
    {
        var size = GetSize(settings.SizePreset);
        settings.Width = size.Width;
        settings.Height = size.Height;

        if (_appWindow is not null)
        {
            _appWindow.Resize(size);
            MoveToAnchor(settings, size);
        }

        WidgetShell.Padding = settings.SizePreset switch
        {
            WidgetSizePreset.Small => new Thickness(12, 8, 12, 8),
            WidgetSizePreset.Large => new Thickness(16, 12, 16, 12),
            _ => new Thickness(14, 10, 14, 10)
        };

        ActionText.FontSize = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 11,
            WidgetSizePreset.Large => 14,
            _ => 12
        };
        ActionText.LineHeight = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 14,
            WidgetSizePreset.Large => 18,
            _ => 15
        };
        TimeText.FontSize = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 22,
            WidgetSizePreset.Large => 34,
            _ => 27
        };
        TimeText.LineHeight = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 25,
            WidgetSizePreset.Large => 38,
            _ => 31
        };
        ContextText.FontSize = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 10,
            WidgetSizePreset.Large => 12,
            _ => 11
        };
    }

    public void SetScheduleText(string actionText, string timeText, string contextText)
    {
        ActionText.Text = actionText;
        TimeText.Text = timeText;
        ContextText.Text = contextText;
    }

    private void ConfigureAppWindow()
    {
        var handle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
    }

    private void MoveToAnchor(WidgetSettings settings, SizeInt32 size)
    {
        if (_appWindow is null)
        {
            return;
        }

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        const int margin = 18;

        var x = settings.Anchor is WidgetAnchor.TopRight or WidgetAnchor.BottomRight
            ? workArea.X + workArea.Width - size.Width - margin
            : workArea.X + margin;
        var y = settings.Anchor is WidgetAnchor.BottomLeft or WidgetAnchor.BottomRight
            ? workArea.Y + workArea.Height - size.Height - margin
            : workArea.Y + margin;

        settings.Left = x;
        settings.Top = y;
        _appWindow.Move(new PointInt32(x, y));
    }

    private static SizeInt32 GetSize(WidgetSizePreset preset)
        => preset switch
        {
            WidgetSizePreset.Small => new SizeInt32(286, 72),
            WidgetSizePreset.Large => new SizeInt32(390, 104),
            _ => new SizeInt32(326, 84)
        };
}
