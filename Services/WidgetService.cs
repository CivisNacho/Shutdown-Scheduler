using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Persistence;
using Shutdown_Scheduler.Views;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Shutdown_Scheduler.Services;

public sealed class WidgetService
{
    private const double WidgetEdgeMargin = 8;
    private readonly WidgetSettingsStore _store;
    private readonly LocalizationService _loc;
    private WidgetWindow? _window;

    public WidgetService(WidgetSettingsStore store, LocalizationService loc)
    {
        _store = store;
        _loc = loc;
        Settings = store.Load();
    }

    public WidgetSettings Settings { get; }

    public void ApplySettings()
    {
        ApplyPresetSize();
        ClampToScreens();
        _store.Save(Settings);
        if (Settings.Enabled)
        {
            EnsureWindow();
            ApplyGeometry();
            _window!.SetText(_loc.T("AppTitle"));
            if (!_window.IsVisible) _window.Show();
        }
        else
        {
            _window?.Close();
            _window = null;
        }
    }

    public void Update(ActiveSchedule? schedule)
    {
        if (!Settings.Enabled && schedule is null)
        {
            _window?.Hide();
            return;
        }

        EnsureWindow();
        ApplyGeometry();

        if (schedule is null)
        {
            _window!.SetText(_loc.T("AppTitle"));
            if (!_window.IsVisible) _window.Show();
            return;
        }

        var action = _loc.ActionLabel(schedule.Request.Action);
        var text = Settings.DisplayMode == WidgetDisplayMode.Countdown
            ? $"{action} {_loc.T("WidgetIn")} {FormatRemaining(schedule.TargetTime - DateTime.Now)}"
            : $"{action} {_loc.T("WidgetAt")} {schedule.TargetTime:HH:mm}";
        _window!.SetText(text);
        if (!_window.IsVisible) _window.Show();
    }

    private void EnsureWindow()
    {
        _window ??= new WidgetWindow();
    }

    private void ApplyGeometry()
    {
        if (_window is null) return;
        var width = Math.Max(180, Settings.Width);
        var height = Math.Max(48, Settings.Height);
        var bounds = GetWorkingBoundsInDip();

        _window.Width = width;
        _window.Height = height;

        Settings.Left = Settings.Anchor switch
        {
            WidgetAnchor.TopLeft or WidgetAnchor.BottomLeft => bounds.Left + WidgetEdgeMargin,
            _ => bounds.Right - width - WidgetEdgeMargin
        };

        Settings.Top = Settings.Anchor switch
        {
            WidgetAnchor.TopLeft or WidgetAnchor.TopRight => bounds.Top + WidgetEdgeMargin,
            _ => bounds.Bottom - height - WidgetEdgeMargin
        };

        _window.Left = Settings.Left;
        _window.Top = Settings.Top;
    }

    private void ApplyPresetSize()
    {
        (Settings.Width, Settings.Height) = Settings.SizePreset switch
        {
            WidgetSizePreset.Small => (220, 72),
            WidgetSizePreset.Large => (420, 160),
            _ => (320, 120)
        };
    }

    private void ClampToScreens()
    {
        var bounds = GetWorkingBoundsInDip();

        var maxLeft = bounds.Right - Math.Max(180, Settings.Width);
        var maxTop = bounds.Bottom - Math.Max(48, Settings.Height);
        Settings.Left = Math.Clamp(Settings.Left, bounds.Left, Math.Max(bounds.Left, maxLeft));
        Settings.Top = Math.Clamp(Settings.Top, bounds.Top, Math.Max(bounds.Top, maxTop));
    }

    private Rect GetWorkingBoundsInDip()
    {
        var rawBounds = Settings.ShowOnAllScreens
            ? Forms.SystemInformation.VirtualScreen
            : Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.SystemInformation.VirtualScreen;

        // WinForms gives physical pixels. WPF window coordinates use DIPs.
        var dpi = _window is null ? new DpiScale(1, 1) : VisualTreeHelper.GetDpi(_window);
        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;

        return new Rect(
            rawBounds.Left / scaleX,
            rawBounds.Top / scaleY,
            rawBounds.Width / scaleX,
            rawBounds.Height / scaleY);
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}
