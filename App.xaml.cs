using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Shutdown_Scheduler.Persistence;
using Shutdown_Scheduler.Services;
using Shutdown_Scheduler.ViewModels;

namespace Shutdown_Scheduler_WinUI
{
    public partial class App : Application
    {
        private Window? _window;
        private CpuMonitorService? _cpu;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            var localization = new LocalizationService();
            _cpu = new CpuMonitorService();
            var network = new NetworkMonitorService();
            var conditions = new ConditionEvaluationService(_cpu, network, localization);
            var executor = new ActionExecutionService();
            var scheduler = new SchedulingService(
                conditions,
                executor,
                localization,
                new WinUiAppTimerFactory(dispatcherQueue));

            var widget = new WinUiWidgetService(new WidgetSettingsStore(), localization, dispatcherQueue);
            var notifications = new WinUiUserNotificationService(() => _window?.Content.XamlRoot, dispatcherQueue);
            var viewModel = new MainViewModel(scheduler, widget, localization, notifications);

            _window = new MainWindow(viewModel, scheduler, localization);
            _window.Closed += (_, _) => _cpu?.Dispose();
            _window.Activate();
        }
    }
}
