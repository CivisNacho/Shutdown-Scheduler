using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Shutdown_Scheduler.Models;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace Shutdown_Scheduler_WinUI.Views;

public sealed partial class WidgetWindow : Window
{
    private nint _windowHandle;
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
        var iconSize = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 8,
            WidgetSizePreset.Large => 12,
            _ => 10
        };
        ConditionIconPrimaryText.FontSize = iconSize;
        ConditionIconSecondaryText.FontSize = iconSize;
    }

    public void SetScheduleText(string actionText, string timeText, string conditionIcons)
    {
        ActionText.Text = actionText;
        TimeText.Text = timeText;
        var icons = (conditionIcons ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        ConditionIconPrimaryText.Text = icons.Length > 0 ? icons[0] : string.Empty;
        ConditionIconSecondaryText.Text = icons.Length > 1 ? icons[1] : string.Empty;
    }

    public void ShowWidget()
    {
        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwShowNoActivate);
    }

    public void HideWidget()
    {
        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwHide);
    }

    private void ConfigureAppWindow()
    {
        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        NativeMethods.HideFromTaskbar(_windowHandle);

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

    private static partial class NativeMethods
    {
        public const int SwHide = 0;
        public const int SwShowNoActivate = 4;
        private const int GwlExStyle = -20;
        private const nint WsExAppWindow = 0x00040000;
        private const nint WsExToolWindow = 0x00000080;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpFrameChanged = 0x0020;

        public static void HideFromTaskbar(nint hWnd)
        {
            var style = GetWindowLongPtr(hWnd, GwlExStyle);
            SetWindowLongPtr(hWnd, GwlExStyle, (style & ~WsExAppWindow) | WsExToolWindow);
            SetWindowPos(hWnd, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint hWnd, int nCmdShow);
    }
}
