using System.Windows;
using Shutdown_Scheduler.Persistence;
using Shutdown_Scheduler.Services;
using Shutdown_Scheduler.ViewModels;

namespace Shutdown_Scheduler;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private CpuMonitorService? _cpu;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var localization = new LocalizationService();
        _cpu = new CpuMonitorService();
        var network = new NetworkMonitorService();
        var conditions = new ConditionEvaluationService(_cpu, network, localization);
        var executor = new ActionExecutionService();
        var scheduler = new SchedulingService(conditions, executor, localization);
        var widget = new WidgetService(new WidgetSettingsStore(), localization);
        _tray = new TrayService(localization);

        var viewModel = new MainViewModel(scheduler, widget, localization);
        var mainWindow = new MainWindow(scheduler, _tray, localization)
        {
            DataContext = viewModel
        };

        _tray.OpenRequested += (_, _) =>
        {
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        };

        _tray.CancelRequested += (_, _) => scheduler.Cancel();

        _tray.ExitRequested += (_, _) =>
        {
            scheduler.Cancel();
            Shutdown();
        };

        scheduler.ScheduleChanged += (_, _) =>
        {
            if (scheduler.ActiveSchedule is not null)
            {
                _tray.Show(true);
            }
            else if (mainWindow.IsVisible)
            {
                _tray.Hide();
            }
        };

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _cpu?.Dispose();
        base.OnExit(e);
    }
}
