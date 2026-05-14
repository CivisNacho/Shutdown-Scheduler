# GPT Instructions: Rework Shutdown Scheduler From WPF To WinUI 3

You are helping migrate this project from WPF to WinUI 3. The current repo has already been prepared so reusable code lives in `ShutdownScheduler.Core`, while the existing WPF project remains as a working shell. Your job is to create a WinUI 3 shell that reuses the core project and only rewrites the UI/framework-specific parts.

## Primary Goal

Create a WinUI 3 version of the app that preserves current behavior:

- Schedule shutdown, restart, suspend, hibernate, log off, and lock session actions.
- Support normal and forced/admin execution where available.
- Support a target date/time within the next 7 days.
- Support relative minutes input that updates the target time.
- Support optional network and CPU conditions.
- Show active schedule summary, status, and remaining time.
- Show a warning shortly before execution with postpone and cancel options.
- Keep widget settings persistence and implement a WinUI equivalent for the floating widget if practical.

Do not rewrite the business logic already in `ShutdownScheduler.Core`. Treat that project as the source of truth.

## Reuse Exactly From `ShutdownScheduler.Core`

Reference `ShutdownScheduler.Core` from the new WinUI 3 project and reuse these types directly:

- `Shutdown_Scheduler.Models`
  - `PowerAction`
  - `ScheduleMode`
  - `ExecutionMode`
  - `ScheduleStatus`
  - `NetworkUnit`
  - `WidgetDisplayMode`
  - `WidgetSizePreset`
  - `WidgetAnchor`
  - `OptionItem<T>`
  - `ScheduleRequest`
  - `ActiveSchedule`
  - `ConditionEvaluationResult`
  - `WidgetSettings`
- `Shutdown_Scheduler.Services`
  - `ActionExecutionService`
  - `CpuMonitorService`
  - `NetworkMonitorService`
  - `ConditionEvaluationService`
  - `LocalizationService`
  - `SchedulingService`
  - `RemainingTimeFormatter`
  - `IAppTimer`
  - `IAppTimerFactory`
  - `IUserNotificationService`
  - `IWidgetService`
- `Shutdown_Scheduler.Persistence`
  - `WidgetSettingsStore`
- `Shutdown_Scheduler.ViewModels`
  - `MainViewModel`
  - `ObservableObject`
  - `RelayCommand`

If you find a missing UI-neutral behavior, add it to `ShutdownScheduler.Core` behind an interface or helper. Do not add WinUI, WPF, or Windows Forms references to `ShutdownScheduler.Core`.

## Do Not Move These WPF-Specific Files Into WinUI

These files are useful as behavior/design references only. Recreate their functionality in WinUI instead of copying them:

- `App.xaml`
- `App.xaml.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Controls/DateTimePicker.xaml`
- `Controls/DateTimePicker.xaml.cs`
- `Views/WarningWindow.xaml`
- `Views/WarningWindow.xaml.cs`
- `Views/WidgetWindow.xaml`
- `Views/WidgetWindow.xaml.cs`
- `Services/TrayService.cs`
- `Services/WidgetService.cs`
- `Services/WpfAppTimer.cs`
- `Services/WpfUserNotificationService.cs`

Any type that depends on `System.Windows`, `System.Windows.Controls`, `System.Windows.Threading`, `DependencyProperty`, `RoutedEventArgs`, `Window`, or WPF `MessageBox` must stay out of the WinUI project.

## New WinUI Project Structure

Create a new WinUI 3 project beside the existing WPF project. Suggested project name:

- `ShutdownScheduler.WinUI`

Add it to `Shutdown Scheduler.slnx` and reference:

- `ShutdownScheduler.Core/ShutdownScheduler.Core.csproj`

Keep the WPF project intact until the WinUI version is verified. Do not delete WPF files during the first WinUI migration.

Recommended WinUI structure:

- `App.xaml`
- `App.xaml.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Services/WinUiAppTimer.cs`
- `Services/WinUiUserNotificationService.cs`
- `Services/WinUiWidgetService.cs`
- `Views/WarningDialog.xaml`
- `Views/WarningDialog.xaml.cs`
- Optional: `Controls/DateTimePicker.xaml`
- Optional: `Services/TrayService.cs` if tray support is implemented for WinUI

## Required WinUI Adapters

Implement these adapters in the WinUI project.

### `WinUiAppTimer`

Implement `IAppTimer` and `IAppTimerFactory` using WinUI's dispatcher queue timer:

```csharp
using Microsoft.UI.Dispatching;
using Shutdown_Scheduler.Services;

namespace Shutdown_Scheduler.Services;

public sealed class WinUiAppTimerFactory : IAppTimerFactory
{
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUiAppTimerFactory(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public IAppTimer Create(TimeSpan interval)
        => new WinUiAppTimer(_dispatcherQueue, interval);
}

public sealed class WinUiAppTimer : IAppTimer
{
    private readonly DispatcherQueueTimer _timer;

    public WinUiAppTimer(DispatcherQueue dispatcherQueue, TimeSpan interval)
    {
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = interval;
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
```

### `WinUiUserNotificationService`

Implement `IUserNotificationService`. For validation warnings, use a WinUI-friendly surface such as `ContentDialog`.

Important: `ContentDialog.XamlRoot` must be set from the current window content. If showing dialogs from the view model becomes awkward, queue the dialog request through the main window or an app-level notification service.

### `WinUiWidgetService`

Implement `IWidgetService`.

Minimum acceptable implementation:

- Load and save `WidgetSettings` using `WidgetSettingsStore`.
- Implement `ApplySettings()`.
- Implement `Update(ActiveSchedule? schedule)`.
- It is acceptable for v1 to be a no-op visual service that preserves settings and does not crash.

Preferred implementation:

- Create a small always-on-top WinUI/AppWindow widget.
- Reuse `WidgetSettings`, `WidgetDisplayMode`, `WidgetSizePreset`, `WidgetAnchor`, and `RemainingTimeFormatter`.
- Preserve the same text behavior:
  - Countdown mode: `{Action} in HH:mm:ss`
  - Execution time mode: `{Action} at HH:mm`

## App Startup Wiring

In the WinUI app startup, create the same core service graph:

```csharp
var localization = new LocalizationService();
var cpu = new CpuMonitorService();
var network = new NetworkMonitorService();
var conditions = new ConditionEvaluationService(cpu, network, localization);
var executor = new ActionExecutionService();
var scheduler = new SchedulingService(
    conditions,
    executor,
    localization,
    new WinUiAppTimerFactory(DispatcherQueue.GetForCurrentThread()));

var widget = new WinUiWidgetService(new WidgetSettingsStore(), localization);
var notifications = new WinUiUserNotificationService(/* current window or dialog host */);

var viewModel = new MainViewModel(
    scheduler,
    widget,
    localization,
    notifications);
```

Assign `viewModel` as the `DataContext`/binding source for the WinUI main window.

Dispose `CpuMonitorService` on app exit.

## UI Migration Guidance

Use `MainWindow.xaml` from the WPF project as a behavioral reference, not as code to paste.

Recreate the main WinUI screen with these sections:

- Action
  - Action dropdown bound to `Actions`.
  - Forced/admin yes/no selector bound to `IsForcedAdmin` and `IsNormalExecution`.
  - Hide forced/admin selector when `IsForcedModeAvailable` is false.
- Target time
  - Date and time picker bound to `SelectedTargetDateTime`.
  - Relative minutes input bound to `RelativeMinutes`.
- Conditions
  - Network condition toggle bound to `NetworkConditionEnabled`.
  - Network threshold input bound to `NetworkThreshold`.
  - Network units bound to `NetworkUnits` and `SelectedNetworkUnit`.
  - CPU condition toggle bound to `CpuConditionEnabled`.
  - CPU threshold input bound to `CpuThreshold`.
- Widget settings
  - Enabled toggle bound to `WidgetEnabled`.
  - Show on all screens bound to `WidgetShowOnAllScreens`.
  - Size preset bound to `WidgetSizePresets` and `SelectedWidgetSizePreset`.
  - Position bound to `WidgetAnchors` and `SelectedWidgetAnchor`.
- Summary
  - Selected action label from `SelectedAction.Label`.
  - Active schedule text from `ActiveSummary`.
  - Status text from `StatusSummary`.
  - Schedule button bound to `ScheduleCommand`.
  - Cancel button bound to `CancelCommand` if you expose it in the UI.

Do not depend on WPF-specific binding features or WPF resource syntax. Translate styles to WinUI resources.

## Warning Flow

`SchedulingService.WarningDue` fires when the action is close. The WinUI shell must subscribe to it.

On warning:

- Show a WinUI `ContentDialog` or equivalent.
- Display action and remaining time using `LocalizationService` and `RemainingTimeFormatter`.
- If user chooses postpone, call `scheduler.Postpone()`.
- If user chooses cancel, call `scheduler.Cancel()`.

Also subscribe to `SchedulingService.Error` and show an app-level warning through the WinUI notification service.

## Tray Behavior

The WPF project currently uses Windows Forms `NotifyIcon`. WinUI 3 does not have a direct built-in tray abstraction.

For v1, tray behavior may be deferred if the main scheduling flow works.

If tray behavior is required:

- Implement it in the WinUI project only.
- Do not put tray code into `ShutdownScheduler.Core`.
- You may use a Windows App SDK-compatible tray approach, Win32 interop, or a small Windows Forms `NotifyIcon` adapter if the project configuration supports it.
- Preserve these events conceptually:
  - Open app
  - Cancel schedule
  - Cancel and exit / exit

## Date/Time Picker

The WPF project has a custom `Controls/DateTimePicker`.

For WinUI:

- Prefer native WinUI `DatePicker` + `TimePicker` controls if they can bind cleanly to `SelectedTargetDateTime`.
- If needed, create a small WinUI user control that exposes a `DateTime? Value` dependency property.
- Preserve these constraints:
  - Minimum is `MainViewModel.MinimumTargetDateTime`.
  - Maximum is `MainViewModel.MaximumTargetDateTime`.
  - Minute step is 5 minutes.
  - Null should not be allowed for the main target picker.

## Validation And Behavior Notes

`MainViewModel` currently validates:

- Target date/time must exist.
- Target date/time must be in the future.
- Target date/time must be within the next 7 days.
- Network threshold must be zero or greater.
- CPU threshold must be between 0 and 100.

Do not duplicate this validation in the WinUI code-behind unless needed for UI affordances. Let the core view model own scheduling validation.

## Build And Verification

After implementation:

1. Build the solution.
2. Confirm `ShutdownScheduler.Core` still has no WPF or WinUI references.
3. Launch the WinUI app.
4. Verify the main screen loads and bindings populate:
   - Actions
   - Network units
   - Widget size presets
   - Widget anchors
5. Schedule a harmless action such as lock session only if safe, or test with action execution temporarily disabled behind a local debug switch.
6. Verify invalid inputs show a warning.
7. Verify cancel updates the active schedule state.
8. Verify widget settings load/save without crashing.
9. Verify app exit disposes the CPU monitor.

## Hard Rules

- Do not add `System.Windows` or WPF references to `ShutdownScheduler.Core`.
- Do not rewrite scheduling, localization, action execution, persistence, or condition evaluation unless a compile issue requires a small framework-neutral fix.
- Do not delete the WPF app until the WinUI app is verified.
- Keep namespaces compatible with the current core namespace: `Shutdown_Scheduler`.
- Keep the code path for actual power actions explicit and easy to audit.

