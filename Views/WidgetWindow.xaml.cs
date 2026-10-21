using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    private readonly (Border Chip, FontIcon Icon)[] _conditionChips;

    public WidgetWindow()
    {
        InitializeComponent();
        _conditionChips =
        [
            (ConditionChip1, ConditionIcon1),
            (ConditionChip2, ConditionIcon2),
            (ConditionChip3, ConditionIcon3),
            (ConditionChip4, ConditionIcon4)
        ];
        ConfigureAppWindow();
        Root.ActualThemeChanged += (_, _) => ApplyFrameBorderColor();
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

        Root.Padding = settings.SizePreset switch
        {
            WidgetSizePreset.Small => new Thickness(12, 8, 12, 8),
            WidgetSizePreset.Large => new Thickness(18, 12, 18, 12),
            _ => new Thickness(14, 10, 14, 10)
        };

        var badgeSize = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 34,
            WidgetSizePreset.Large => 50,
            _ => 42
        };
        ActionBadge.Width = badgeSize;
        ActionBadge.Height = badgeSize;
        ActionBadge.CornerRadius = new CornerRadius(badgeSize / 4.0);
        ActionIcon.FontSize = Math.Round(badgeSize * 0.44);

        ActionText.FontSize = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 11,
            WidgetSizePreset.Large => 13,
            _ => 12
        };
        TimeText.FontSize = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 21,
            WidgetSizePreset.Large => 32,
            _ => 26
        };
        TimeText.LineHeight = Math.Round(TimeText.FontSize * 1.15);

        var chipSize = settings.SizePreset switch
        {
            WidgetSizePreset.Small => 20,
            WidgetSizePreset.Large => 26,
            _ => 22
        };
        foreach (var (chip, icon) in _conditionChips)
        {
            chip.Width = chipSize;
            chip.Height = chipSize;
            chip.CornerRadius = new CornerRadius(chipSize / 2.0);
            icon.FontSize = chipSize / 2;
        }
    }

    public void SetScheduleText(string actionText, string actionGlyph, string timeText, string conditionIcons)
    {
        ActionText.Text = actionText;
        ActionIcon.Glyph = actionGlyph;
        TimeText.Text = timeText;
        var icons = (conditionIcons ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < _conditionChips.Length; i++)
        {
            var (chip, icon) = _conditionChips[i];
            var visible = i < icons.Length;
            chip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = visible ? icons[i] : string.Empty;
        }
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

        // SetBorderAndTitleBar(false, false) still leaves WS_DLGFRAME, which draws a light frame around the content.
        NativeMethods.RemoveFrame(_windowHandle);

        // Let DWM round and outline the window: XAML can't make the window corners transparent,
        // so rounding the content instead leaves square light-colored corners behind it.
        NativeMethods.SetRoundedCorners(_windowHandle);
        ApplyFrameBorderColor();
    }

    private void ApplyFrameBorderColor()
    {
        if (Root.BorderBrush is SolidColorBrush brush)
        {
            NativeMethods.SetBorderColor(_windowHandle, brush.Color);
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
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const nint WsBorder = 0x00800000;
        private const nint WsDlgFrame = 0x00400000;
        private const nint WsThickFrame = 0x00040000;
        private const nint WsExAppWindow = 0x00040000;
        private const nint WsExToolWindow = 0x00000080;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpFrameChanged = 0x0020;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaBorderColor = 34;
        private const int DwmwcpRound = 2;

        public static void HideFromTaskbar(nint hWnd)
        {
            var style = GetWindowLongPtr(hWnd, GwlExStyle);
            SetWindowLongPtr(hWnd, GwlExStyle, (style & ~WsExAppWindow) | WsExToolWindow);
            SetWindowPos(hWnd, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }

        public static void RemoveFrame(nint hWnd)
        {
            var style = GetWindowLongPtr(hWnd, GwlStyle);
            SetWindowLongPtr(hWnd, GwlStyle, style & ~(WsBorder | WsDlgFrame | WsThickFrame));
            SetWindowPos(hWnd, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }

        // Both DWM attributes are Windows 11+; on Windows 10 the calls fail harmlessly and the widget stays square.
        public static void SetRoundedCorners(nint hWnd)
        {
            var preference = DwmwcpRound;
            DwmSetWindowAttribute(hWnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }

        public static void SetBorderColor(nint hWnd, Windows.UI.Color color)
        {
            var colorRef = color.R | (color.G << 8) | (color.B << 16);
            DwmSetWindowAttribute(hWnd, DwmwaBorderColor, ref colorRef, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint hWnd, int attribute, ref int value, int size);

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
