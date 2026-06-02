using System;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Shutdown_Scheduler.Persistence;
using Shutdown_Scheduler.ViewModels;
using Shutdown_Scheduler_WinUI;
using Shutdown_Scheduler_WinUI.Services;
using Windows.System.UserProfile;

namespace Shutdown_Scheduler.Services;

public sealed class AppSession : IDisposable
{
    private readonly LocalizationService _localization;
    private readonly AppDetectionExclusionStore _appDetectionExclusions;
    private readonly RunningAppDiscoveryService _runningApps;
    private readonly CpuMonitorService _cpu;
    private readonly BatteryMonitorService _battery;
    private readonly SchedulingService _scheduler;
    private readonly WinUiWidgetService _widget;
    private readonly WinUiTrayService _tray;
    private readonly WinUiAppIconCache _appIconCache;
    private readonly MainViewModel _viewModel;
    private MainWindow? _mainWindow;
    private bool _isExiting;
    private bool _isDisposed;

    public AppSession(DispatcherQueue dispatcherQueue)
    {
        _localization = new LocalizationService(ResolveUserPreferredCulture());
        _appDetectionExclusions = new AppDetectionExclusionStore();
        _runningApps = new RunningAppDiscoveryService(_appDetectionExclusions);
        _cpu = new CpuMonitorService();
        _battery = new BatteryMonitorService();
        var network = new NetworkMonitorService();
        var conditions = new ConditionEvaluationService(_cpu, network, _battery, _localization);
        var executor = new ActionExecutionService();
        _scheduler = new SchedulingService(
            conditions,
            executor,
            _localization,
            new WinUiAppTimerFactory(dispatcherQueue));
        _widget = new WinUiWidgetService(new WidgetSettingsStore(), _localization, dispatcherQueue);
        _tray = new WinUiTrayService(
            _localization,
            ShowMainWindow,
            ToggleWidgetEnabled,
            CancelSchedule,
            ExitApplication);
        _appIconCache = new WinUiAppIconCache();

        var notifications = new WinUiUserNotificationService(() => _mainWindow?.Content.XamlRoot, dispatcherQueue);
        _viewModel = new MainViewModel(_scheduler, _widget, _localization, notifications, _battery);

        _scheduler.ScheduleChanged += OnScheduleChanged;
        _scheduler.Tick += OnSchedulerTick;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RefreshBackgroundSurfaces();
    }

    public bool IsExiting => _isExiting;

    public void ShowMainWindow()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Activate();
            _mainWindow.BringToForeground();
            RefreshTray();
            return;
        }

        _mainWindow = new MainWindow(_viewModel, _scheduler, _localization, this, _runningApps, _appIconCache);
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.Activate();
        RefreshTray();
    }

    public void CloseMainWindowToTray(MainWindow window)
    {
        if (!ReferenceEquals(window, _mainWindow))
        {
            return;
        }
    }

    public void ToggleWidgetEnabled()
    {
        _viewModel.WidgetEnabled = !_viewModel.WidgetEnabled;
        RefreshBackgroundSurfaces();
    }

    public void CancelSchedule()
    {
        _scheduler.Cancel();
        RefreshBackgroundSurfaces();
    }

    public void ExitApplication()
    {
        if (_isDisposed)
        {
            return;
        }

        _isExiting = true;
        _scheduler.Cancel();
        Dispose();
        Application.Current.Exit();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _scheduler.ScheduleChanged -= OnScheduleChanged;
        _scheduler.Tick -= OnSchedulerTick;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _widget.Dispose();
        _tray.Dispose();
        _cpu.Dispose();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is MainWindow window)
        {
            window.Closed -= OnMainWindowClosed;
        }

        _mainWindow = null;

        if (_isExiting || _scheduler.ActiveSchedule is null)
        {
            ExitApplication();
            return;
        }

        RefreshTray();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsForcedAdmin)) return;
        if (!_viewModel.IsForcedAdmin) return;
        if (ElevationService.IsElevated) return;

        bool restarted = ElevationService.TryRestartAsAdmin();
        if (restarted)
        {
            ExitApplication();
        }
        else
        {
            // User cancelled UAC — revert the toggle
            _viewModel.IsForcedAdmin = false;
        }
    }

    private void OnScheduleChanged(object? sender, EventArgs e)
    {
        RefreshBackgroundSurfaces();
    }

    private void OnSchedulerTick(object? sender, EventArgs e)
    {
        _widget.Update(_scheduler.ActiveSchedule);
    }

    private void RefreshBackgroundSurfaces()
    {
        _widget.Update(_scheduler.ActiveSchedule);
        RefreshTray();
    }

    private void RefreshTray()
    {
        _tray.Update(_scheduler.ActiveSchedule is not null, _widget.Settings.Enabled, _mainWindow is not null);
    }

    private static CultureInfo ResolveUserPreferredCulture()
    {
        foreach (var language in GlobalizationPreferences.Languages)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(language);
                var languageTag = culture.TwoLetterISOLanguageName;
                if (languageTag.Equals("es", StringComparison.OrdinalIgnoreCase) ||
                    languageTag.Equals("en", StringComparison.OrdinalIgnoreCase))
                {
                    return culture;
                }
            }
            catch (CultureNotFoundException)
            {
                // Ignore unsupported language tags and continue through the preference list.
            }
        }

        return CultureInfo.GetCultureInfo("en-US");
    }
}
