using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Shutdown_Scheduler.Services;
using Shutdown_Scheduler.Views;

namespace Shutdown_Scheduler;

public partial class MainWindow : Window
{
    private readonly SchedulingService _scheduler;
    private readonly TrayService _tray;
    private readonly LocalizationService _loc;

    public MainWindow(SchedulingService scheduler, TrayService tray, LocalizationService loc)
    {
        _scheduler = scheduler;
        _tray = tray;
        _loc = loc;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();

        _scheduler.WarningDue += Scheduler_WarningDue;
        _scheduler.Error += (_, message) => Dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(this, message, _loc.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Warning));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_scheduler.ActiveSchedule is not null)
        {
            e.Cancel = true;
            Hide();
            _tray.Show(true);
            return;
        }

        base.OnClosing(e);
    }

    private void Scheduler_WarningDue(object? sender, Models.ActiveSchedule schedule)
    {
        Dispatcher.Invoke(() =>
        {
            var warning = new WarningWindow(schedule, _loc)
            {
                Owner = IsVisible ? this : null
            };

            warning.ShowDialog();
            if (warning.ShouldPostpone) _scheduler.Postpone();
            if (warning.ShouldCancel) _scheduler.Cancel();
        });
    }

    private void ApplyDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    private void Button_Click(object sender, RoutedEventArgs e)
    {

    }

    private void DateTimePicker_Loaded(object sender, RoutedEventArgs e)
    {

    }
}
