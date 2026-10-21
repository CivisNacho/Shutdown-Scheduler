# Shutdown Scheduler App Tour

This document walks through the current repo as a codebase, with the active WinUI shell first, then the shared `ShutdownScheduler.Core` layer, then the legacy WPF-era files that remain as references.

## Big Picture

- `App.xaml` and `App.xaml.cs` start the WinUI app.
- `AppSession` wires together localization, timers, monitoring, scheduling, notifications, the widget, and the tray.
- `MainWindow` is the main control surface for scheduling.
- `SchedulingService` owns the countdown loop and action execution timing.
- `MainViewModel` holds most of the user-facing state and validation.
- `MetricServices.cs` contains the CPU, network, battery, and condition-evaluation logic.
- `Persistence` stores the widget settings and the app-exclusion list for close-app detection.

## Active WinUI Shell

### `App.xaml`
- Declares the WinUI application resources.
- Sets the default theme to dark.
- Merges `Themes/CalmGlassTheme.xaml`.
- Registers `BooleanToVisibilityConverter`.

### `App.xaml.cs`
- `App` creates the app-level `AppSession` on launch.
- `App()` initializes the WinUI application.
- `OnLaunched(...)` gets the current dispatcher queue, creates `AppSession`, and shows the main window.

### `MainWindow.xaml`
- Defines the main WinUI screen.
- Contains the hero header with remaining time, summary, schedule, and cancel buttons.
- Contains the schedule section with action selection, forced-admin toggle, close-app picker, date/time inputs, and relative minutes input.
- Contains the conditions section with network, CPU, and battery-style condition editors.
- Contains the widget settings section.
- Uses adaptive states so the layout compresses from three columns to one column.
- Uses a custom app-target item template with icon loading feedback.

### `MainWindow.xaml.cs`
- `MainWindow(...)` wires the view model, scheduler, localization, session, app discovery, and icon cache.
- `OnLoaded(...)` syncs picker values, applies title-bar styling, and warms the close-app picker once.
- `ConfigureWindowSize()` locks the window to a fixed size, removes maximize, hooks close behavior, and installs the subclass proc.
- `OnClosed(...)` detaches events, cancels background work, disposes resources, and destroys icon handles.
- `OnAppWindowClosing(...)` routes close-to-tray behavior when there is an active schedule.
- `BringToForeground()` forces the window to the front.
- `OnRootActualThemeChanged(...)` reapplies title-bar colors when the theme changes.
- `ApplyTitleBarTheme()` maps theme resources onto the WinUI title bar colors.
- `ApplyWindowIcon()` loads small and large icon handles and sends them to the native window.
- `ResolveThemeColor(...)` looks up a theme brush and returns its color.
- `TryResolveBrush(...)` recursively searches merged dictionaries for a brush resource.
- `WindowSubclassProc(...)` clamps the window size by handling `WM_GETMINMAXINFO`.
- `OnTargetDateChanged(...)` pushes picker changes back into the view model.
- `OnRelativeMinutesValueChanged(...)` updates the relative-minute value in the view model and resyncs the UI.
- `OnTargetTimeChanged(...)` pushes time-picker changes back into the view model.
- `OnRefreshCloseAppTargetsClick(...)` refreshes the list of running apps.
- `OnAddConditionClick(...)` opens the add-condition dialog and wires its buttons.
- `OnRemoveConditionClick(...)` removes a condition item from the view model.
- `SyncPickersFromViewModel()` copies the selected target time and relative minutes into the visible controls.
- `SyncViewModelFromPickers()` copies date/time picker changes back into `SelectedTargetDateTime`.
- `OnSchedulerError(...)` shows an error dialog for scheduler failures.
- `OnWarningDue(...)` shows the pre-execution warning dialog and calls postpone or cancel based on the response.
- `ApplyDialogColors(...)` styles WinUI dialogs to match the app theme.
- `ApplyDialogButtonColors(...)` styles dialog buttons to match the app theme.
- `ResolveThemeBrush(...)` returns a brush from the app resource tree with fallback support.
- `OnCloseAppPickerSelectionChanged(...)` maps the selected running app to `AppTarget` in the view model.
- `RefreshCloseAppTargetsAsync()` queries running apps, updates the picker list, preserves selection, and kicks off icon warming.
- `WarmIconsAsync(...)` loads app icons in parallel for the picker.
- `LoadIconAsync(...)` loads a single icon and pushes it to the UI thread.
- `RunOnUiThreadAsync(...)` marshals async UI updates through `DispatcherQueue`.
- `FindBestSelection(...)` tries to preserve the previously selected app after refresh.
- `NativeMethods.RemoveMaximizeButton(...)` strips the maximize box from the window style.
- `NativeMethods.SetWindowSubclass(...)`, `RemoveWindowSubclass(...)`, `DefSubclassProc(...)`, `GetWindowLongPtr(...)`, `SetWindowLongPtr(...)`, `SetWindowPos(...)`, `SetForegroundWindow(...)`, and `SendMessage(...)` provide the native window interop.

### `Views/WidgetWindow.xaml`
- Defines the small floating status widget.
- Shows an action label, a large time value, and up to two stacked condition icons.
- Uses a compact, always-on-top layout with custom theme brushes.

### `Views/WidgetWindow.xaml.cs`
- `WidgetWindow()` initializes the window and applies the app-window configuration.
- `ApplySettings(...)` resizes the widget, updates padding and font sizes, and re-anchors it.
- `SetScheduleText(...)` updates the visible action text, time text, and condition icons.
- `ShowWidget()` shows the window without activating it.
- `HideWidget()` hides the window.
- `ConfigureAppWindow()` turns the window into a borderless always-on-top tool window and removes it from the taskbar.
- `MoveToAnchor(...)` positions the widget based on the selected anchor and screen work area.
- `GetSize(...)` returns the pixel size for each widget preset.
- `NativeMethods.HideFromTaskbar(...)` changes the extended window style so the widget behaves like a tool window.
- `NativeMethods.ShowWindow(...)` calls the native show/hide API.

### `Views/WarningWindow.xaml`
- Defines the legacy WPF-style warning dialog.
- Shows the pending action message and offers postpone or cancel buttons.

### `Views/WarningWindow.xaml.cs`
- `WarningWindow(...)` creates the warning dialog content and localizes the text.
- `Postpone_Click(...)` marks the dialog result as postpone and closes the window.
- `Cancel_Click(...)` marks the dialog result as cancel and closes the window.

### `Controls/DateTimePicker.xaml`
- Defines a custom WPF date/time picker control.
- Uses one popup for time selection and one popup for calendar selection.
- Supports quick stepping, calendar navigation, and a compact text display.

### `Controls/DateTimePicker.xaml.cs`
- `DateTimePicker()` builds weekday labels, syncs from the current value, and creates the calendar grid.
- `Value`, `Minimum`, `Maximum`, `MinuteStep`, `DisplayFormat`, and `IsNullable` expose the control’s dependency properties.
- `IsPopupOpen` opens or closes the time popup and keeps the date popup mutually exclusive.
- `IsDatePopupOpen` opens or closes the calendar popup and keeps the time popup mutually exclusive.
- `SelectedDate` stores the chosen calendar date.
- `DisplayDate` stores the currently displayed month and rebuilds the month grid when it changes.
- `CalendarTitle` exposes the visible month label.
- `CanNavigatePreviousMonth` and `CanNavigateNextMonth` control month navigation buttons.
- `DisplayText` formats the selected date and time for the collapsed field view.
- `ClearButtonVisibility` hides or shows the clear button based on nullability.
- `CoerceValue(...)` clamps an assigned value into range or supplies the current time when null is not allowed.
- `CoerceMinuteStep(...)` keeps the minute step between 1 and 60.
- `OnValueChanged(...)` syncs the internal state when `Value` changes.
- `OnRangeChanged(...)` re-coerces the selected value and rebuilds the calendar when the range changes.
- `OnMinuteStepChanged(...)` re-syncs the time columns after the minute step changes.
- `OnIsNullableChanged(...)` updates the clear-button visibility when nullability changes.
- `OnDisplayFormatChanged(...)` refreshes the display string when the format changes.
- `SyncFromValue()` copies the selected value into the internal calendar and time state.
- `EnsureCalendarDate()` clamps the selected date into the current min/max range before opening the calendar popup.
- `Clamp(...)` snaps a value to the minute step and enforces min/max bounds.
- `SnapDateTime(...)` rounds a date/time up, down, or to nearest minute step.
- `SnapMinute(...)` rounds a minute to the nearest valid step for the picker columns.
- `SyncPendingFromValue()` converts `Value` into the temporary hour, minute, and AM/PM selection state.
- `RebuildTimeColumns()` repopulates the scrollable hour, minute, and period columns.
- `BuildWeekdayLabels()` builds the localized weekday header row.
- `RebuildCalendar()` creates the 6x7 month grid, selection state, and navigation flags.
- `MonthHasAnySelectableDate(...)` decides whether the previous or next month should be enabled.
- `IsDateInRange(...)` checks whether a calendar day falls inside the allowed range.
- `Wrap(...)` wraps hour and minute values when building the picker columns.
- `HourOption_Click(...)` updates the temporary hour selection.
- `MinuteOption_Click(...)` updates the temporary minute selection.
- `PeriodOption_Click(...)` updates the AM/PM selection.
- `Confirm_Click(...)` writes the chosen time back to `Value` and closes the popup.
- `Cancel_Click(...)` closes the time popup without changing the value.
- `PreviousMonth_Click(...)` moves the calendar view back one month.
- `NextMonth_Click(...)` moves the calendar view forward one month.
- `CalendarDay_Click(...)` writes the selected day back to `Value` and closes the calendar popup.
- `OnPropertyChanged(...)` raises `INotifyPropertyChanged`.
- `TimeColumnOption` is the row model for hour, minute, and period entries.
- `CalendarDayOption` is the row model for the calendar grid.

### `Converters/BooleanToVisibilityConverter.cs`
- `Convert(...)` turns a boolean into `Visible` or `Collapsed`, with optional inversion when the parameter is `Invert`.
- `ConvertBack(...)` returns `true` only when the visibility is `Visible`.

### `Services/AppSession.cs`
- `AppSession(DispatcherQueue)` constructs the whole app graph and wires up scheduler, widget, tray, notifications, and the main view model.
- `IsExiting` exposes whether shutdown is in progress.
- `ShowMainWindow()` opens the main window or brings the existing one forward.
- `CloseMainWindowToTray(...)` is the close-to-tray entry point used by the main window.
- `ToggleWidgetEnabled()` flips the widget on or off and refreshes background surfaces.
- `CancelSchedule()` cancels the current schedule and refreshes the widget and tray.
- `ExitApplication()` cancels the schedule, disposes the session, and exits the app.
- `Dispose()` detaches event handlers and disposes the widget, tray, and CPU monitor.
- `OnMainWindowClosed(...)` clears the main-window reference and decides whether to exit or keep running in the tray.
- `OnScheduleChanged(...)` refreshes the widget and tray whenever schedule state changes.
- `OnSchedulerTick(...)` pushes the active schedule into the widget on every tick.
- `RefreshBackgroundSurfaces()` updates both the widget and tray from the current schedule state.
- `RefreshTray()` shows or hides tray options based on whether a schedule exists, whether the widget is enabled, and whether the main window is open.

### `Services/WinUiTrayService.cs`
- `WinUiTrayService(...)` creates the message-only window, loads the tray icon, and stores the app callbacks.
- `Update(...)` shows, hides, or refreshes the tray icon depending on schedule and window state.
- `ShowContextMenu()` builds and displays the right-click tray menu.
- `ShowBalloonTip(...)` raises a notification balloon from the tray icon.
- `Dispose()` removes the tray icon, destroys the hidden owner window, and frees the icon handle.
- `CreateMessageWindow()` creates the hidden window that receives tray callbacks.
- `WindowSubclassProc(...)` handles tray callback messages and command IDs.
- `CreateIconData(...)` prepares the `NOTIFYICONDATA` payload for Shell_NotifyIcon.
- `ShellNotifyIcon(...)` calls the native shell API wrapper.
- `MenuFlags(...)` returns enabled or grayed menu flags.
- `NativeMethods.SetWindowSubclass(...)`, `RemoveWindowSubclass(...)`, and `DefSubclassProc(...)` support the hidden window message pump.

### `Services/WinUiWidgetService.cs`
- `WinUiWidgetService(...)` loads persisted widget settings and keeps a dispatcher queue for UI updates.
- `Settings` exposes the persisted widget settings object.
- `ApplySettings()` saves settings, hides the widget if disabled, or reapplies the current schedule if one exists.
- `Update(...)` creates or closes the widget window and refreshes its text from the current active schedule.
- `EnsureWindow()` lazily creates the `WidgetWindow`.
- `HideWindow()` hides the widget.
- `CloseWindow()` closes and releases the widget window.
- `UpdateWindowText(...)` formats the action text, countdown or execution time text, and condition icons.
- `GetConditionIcons(...)` maps enabled conditions to glyphs for the widget badge.
- `Dispose()` closes the widget window when the service is torn down.

### `Services/WinUiUserNotificationService.cs`
- `WinUiUserNotificationService(...)` stores the XAML root provider and dispatcher queue.
- `ShowWarning(...)` posts a `ContentDialog` warning on the UI thread.

### `Services/WinUiAppTimer.cs`
- `WinUiAppTimerFactory(...)` stores the dispatcher queue.
- `Create(...)` returns a new `WinUiAppTimer` for the requested interval.
- `WinUiAppTimer(...)` creates the dispatcher timer and relays tick events.
- `Interval` gets or sets the underlying dispatcher timer interval.
- `Tick` exposes timer callbacks.
- `Start()` starts the timer.
- `Stop()` stops the timer.

### `Services/WinUiCloseAppPicker.cs`
- `CloseAppPickerItem(...)` stores one detected app row, including icon and loaded state.
- `Icon` stores the icon image source for the picker row.
- `IsIconLoaded` tells the template when to show the real icon instead of the fallback glyph.
- `CloseAppPickerState(...)` localizes picker labels and listens for item collection changes.
- `Items` holds the running-app choices.
- `SelectedItem` tracks the currently selected app and raises `SelectedItemChanged`.
- `IsLoading` tells the UI whether a refresh is in progress.
- `HasItems` and `HasNoItems` expose list-empty state to the UI.
- `OnItemsChanged(...)` updates the derived item-count properties.
- `SelectedItemChanged` notifies the main window when the selection changes.
- `WinUiAppIconCache` caches executable thumbnails and avoids duplicate loads.
- `GetOrLoadAsync(...)` returns cached icon bytes or loads them on demand.
- `GetOrLoadBytesAsync(...)` coordinates concurrent loads through a `Lazy<Task<byte[]?>>`.
- `LoadBytesAsync(...)` reads the executable thumbnail from storage and returns its bytes.

### `Services/AppIconAssets.cs`
- `IconPath` points to the packaged `Assets/AppIcon.ico`.
- `HasIconFile` reports whether the icon file exists.
- `LoadIconHandle(...)` loads a native icon handle at the requested size.
- `DestroyIconHandle(...)` frees a native icon handle if one was created.

### `Themes/CalmGlassTheme.xaml`
- Defines the app’s shared WinUI theme resources.
- Provides brushes, corner radii, text styles, buttons, list styles, and control defaults for the glass-like dark visual language.
- Supplies the colors used by the main window, widget, dialogs, and controls.

### `Properties/launchSettings.json`
- Defines WinUI launch profiles for packaged and unpackaged runs.

### `Properties/PublishProfiles/win-x86.pubxml`
- Publish profile for x86 builds.

### `Properties/PublishProfiles/win-x64.pubxml`
- Publish profile for x64 builds.

### `Properties/PublishProfiles/win-x64-self-contained.pubxml`
- Publish profile for self-contained x64 builds.

### `Properties/PublishProfiles/win-arm64.pubxml`
- Publish profile for ARM64 builds.

### `Package.appxmanifest`
- Declares the packaged WinUI identity, logos, dependencies, and capabilities.
- Enables `runFullTrust` and `systemAIModels`.

### `app.manifest`
- Declares desktop compatibility and PerMonitorV2 DPI awareness for unpackaged runs.

### `AssemblyInfo.cs`
- Supplies the WPF theme metadata for the legacy project layout.
- Tells WPF to look for theme-specific resources in the app assembly and generic resources in the source assembly.

### `Shutdown Scheduler WinUI.csproj`
- Builds the WinUI shell.
- Targets `net8.0-windows10.0.19041.0`.
- References `Microsoft.WindowsAppSDK` and Windows SDK build tools.
- Includes the WinUI assets and app manifest.
- References `ShutdownScheduler.Core`.
- Excludes the legacy WPF files from compilation.

### `Shutdown Scheduler WinUI.slnx`
- Solution file for the active WinUI app and the shared core project.

### `README.md`
- Gives the short project description and basic build/publish commands.

### `WINUI_MIGRATION_INSTRUCTIONS.md`
- Documents the migration goals and constraints for the WinUI shell.
- Explains which core types should be reused and which WPF files are reference-only.

## Shared Core Project

### `ShutdownScheduler.Core/ShutdownScheduler.Core.csproj`
- Builds the shared business-logic library.
- Targets `net8.0-windows`.
- Pulls in `System.Diagnostics.PerformanceCounter` for CPU sampling.

### `ShutdownScheduler.Core/Models/AppModels.cs`
- `PowerAction` lists the supported actions: shutdown, restart, suspend, hibernate, log off, lock session, and close app.
- `ScheduleMode` lists the supported schedule types.
- `ExecutionMode` distinguishes normal from forced/admin execution.
- `ScheduleStatus` tracks lifecycle state for an active schedule.
- `NetworkUnit` chooses KB/s or MB/s for network thresholds.
- `ConditionType` identifies network, CPU, and battery conditions.
- `WidgetDisplayMode` chooses countdown or execution-time widget text.
- `WidgetSizePreset` defines small, medium, and large widget sizes.
- `WidgetAnchor` defines the four widget corners.
- `OptionItem<T>` pairs a value with a localized label.
- `AppTarget` stores the process name, display label, and executable path for a closable app.
- `ScheduleRequest` carries all inputs needed to run a scheduled action.
- `ActiveSchedule(...)` captures the live schedule state.
- `ActiveSchedule.TargetTime` stores the next execution time.
- `ActiveSchedule.Status` stores the current state.
- `ActiveSchedule.StatusDetails` stores the current status message.
- `ActiveSchedule.WarningShown` tracks whether the warning dialog has already been shown.
- `ActiveSchedule.IsRecurring` reports whether the schedule repeats daily or weekly.
- `ActiveSchedule.OnPropertyChanged(...)` raises `INotifyPropertyChanged`.
- `ConditionEvaluationResult` returns the result of condition checks.
- `WidgetSettings` stores widget enablement, display mode, geometry, and anchor.

### `ShutdownScheduler.Core/ViewModels/ObservableObject.cs`
- `ObservableObject` is the base class for view models and other bindable state holders.
- `SetProperty(...)` updates a field and raises change notifications only when the value actually changes.
- `OnPropertyChanged(...)` raises `PropertyChanged`.
- `RelayCommand(...)` wraps an `Action` and optional `CanExecute` predicate.
- `CanExecute(...)` tells bindings whether a command is currently allowed.
- `Execute(...)` runs the command body.
- `RaiseCanExecuteChanged()` notifies bindings that command availability changed.

### `ShutdownScheduler.Core/ViewModels/MainViewModel.cs`
- `MainViewModel(...)` builds all dropdown choices, initializes defaults, wires commands, and listens to scheduler events.
- `Loc` exposes the localization service to XAML bindings.
- `Actions`, `ScheduleModes`, `ExecutionModes`, `NetworkUnits`, `Conditions`, `WidgetModes`, `WidgetSizePresets`, `WidgetAnchors`, `DayOffsets`, `Weekdays`, `HourOptions`, and `MinuteOptions` expose the selectable UI lists.
- `ScheduleCommand`, `CancelCommand`, `ApplyWidgetCommand`, `AddNetworkConditionCommand`, `AddCpuConditionCommand`, `AddBatteryConditionCommand`, and `RemoveConditionCommand` back the main UI buttons.
- `AppTitle`, `Subtitle`, `ActionLabel`, `ScheduleLabel`, `ExecutionLabel`, `ConditionsLabel`, `AddConditionLabel`, `AddConditionDialogTitle`, `RemoveConditionLabel`, `NoConditionsLabel`, `WidgetLabel`, `ActiveScheduleLabel`, `ScheduleButtonLabel`, `CancelButtonLabel`, `ForceAdminLabel`, `YesLabel`, `NoLabel`, `DayLabel`, `TargetTimeLabel`, `MinutesFromNowLabel`, `ShowOnAllScreensLabel`, `WidgetSizeLabel`, `WidgetPositionLabel`, `ProgramLabel`, and `ProgramPanelText` expose localized strings for the view.
- `MinimumTargetDateTime` and `MaximumTargetDateTime` define the legal target window for scheduling.
- `SelectedAction` updates forced-admin availability and close-app visibility when the action changes.
- `SelectedScheduleMode` tracks the selected schedule mode.
- `SelectedExecutionMode` tracks the selected execution mode.
- `SelectedWidgetMode` applies widget changes whenever the display mode changes.
- `SelectedWidgetSizePreset` persists and applies widget size changes.
- `SelectedWidgetAnchor` persists and applies widget position changes.
- `SelectedDayOffset` and `SelectedWeeklyDay` hold the selected recurrence parameters.
- `ExactTime` stores the exact-time input.
- `RelativeMinutes` parses, clamps, and applies relative-minute input to the target time.
- `TargetTime` keeps the text version of the selected time in sync.
- `SelectedTargetDateTime` stores the canonical target date/time.
- `SelectedHour` and `SelectedMinute` support split time editing.
- `IsForcedAdmin` and `IsNormalExecution` mirror each other for forced/admin execution.
- `SelectedDate`, `DateTimeValue`, `DailyTime`, and `WeeklyTime` are additional date/time inputs used by the view.
- `NetworkConditionEnabled`, `NetworkThreshold`, `CpuConditionEnabled`, `CpuThreshold`, `BatteryConditionEnabled`, and `BatteryThreshold` hold the condition editor state.
- `CanAddNetworkCondition`, `CanAddCpuCondition`, `CanAddBatteryCondition`, `IsBatteryConditionAvailable`, `HasConditions`, `HasNoConditions`, `WidgetEnabled`, `WidgetShowOnAllScreens`, `WidgetLeft`, `WidgetTop`, `WidgetWidth`, `WidgetHeight`, `IsForcedModeAvailable`, and `IsCloseAppAction` expose derived view state.
- `HasActiveSchedule` controls whether the app is already scheduled.
- `ActiveSummary`, `StatusSummary`, and `Remaining` drive the header summary text.
- `Schedule()` validates and submits the request to `SchedulingService`.
- `TryBuildRequest(...)` converts the current UI state into a `ScheduleRequest` or returns a validation message.
- `BuildConditionsSummary(...)` formats the summary text for enabled conditions.
- `BuildDisplaySummary(...)` formats the one-line schedule summary.
- `AddCondition(...)` adds a new condition editor row.
- `RemoveCondition(...)` removes a condition editor row.
- `HasCondition(...)` checks whether a condition type already exists.
- `GetConditionLabel(...)` returns the localized label for a condition type.
- `SyncConditionFlags()` copies the current `Conditions` collection into the enable flags.
- `RefreshConditionState()` updates derived properties and command availability after condition changes.
- `Fail(...)` is the shared validation helper that returns a message and an empty request.
- `ApplyWidget()` copies the widget settings into the widget service and asks it to reapply them.
- `FormatDayOffset(...)` formats the offset labels for the day-offset selector.
- `SetSelectedAppTarget(...)` stores the app selected in the close-app picker.
- `ApplyRelativeMinutesToTarget()` turns the relative-minute input into a concrete target date/time.
- `SyncTargetFromTimeParts()` rebuilds `TargetTime` from the split hour and minute fields.
- `SyncTimePartsFromTarget()` splits `TargetTime` back into hour and minute fields.
- `RefreshActiveState()` translates the current `SchedulingService.ActiveSchedule` into the summary text shown by the UI.
- `ConditionEditorItem(...)` is the per-condition editor row model.
- `ConditionEditorItem.Type` identifies the condition type.
- `ConditionEditorItem.Label` is the row label.
- `ConditionEditorItem.RequiredBelowLabel`, `NetworkShortLabel`, `CpuShortLabel`, `BatteryShortLabel`, and `RemoveConditionLabel` expose localized row text.
- `ConditionEditorItem.NetworkUnits` and `SelectedNetworkUnit` support network-unit selection.
- `ConditionEditorItem.NetworkThreshold`, `CpuThreshold`, and `BatteryThreshold` hold the editable thresholds.
- `ConditionEditorItem.IsNetwork`, `IsCpu`, and `IsBattery` drive row visibility.

### `ShutdownScheduler.Core/Services/SchedulingService.cs`
- `SchedulingService(...)` creates the periodic timer and listens for ticks.
- `ActiveSchedule` exposes the current live schedule.
- `ScheduleChanged`, `Tick`, `WarningDue`, and `Error` expose the service’s events.
- `Schedule(...)` starts a new active schedule.
- `Cancel()` stops the active schedule and the timer.
- `Postpone()` pushes the current schedule out by five minutes and clears the warning state.
- `OnTick(...)` is the heart of the scheduler loop: it handles expiry, warning timing, condition evaluation, action execution, and recurrence.
- `GetNextRecurringTime(...)` calculates the next daily or weekly run.

### `ShutdownScheduler.Core/Services/ActionExecutionService.cs`
- `ExecuteAsync(...)` runs the requested power action on a background thread.
- `CloseApp(...)` tries to close the selected process gracefully and falls back to force-killing it if needed.
- `StartShutdown(...)` launches `shutdown.exe` with the correct flags.
- `SupportsForcedMode(...)` says which actions can use forced/admin mode.

### `ShutdownScheduler.Core/Services/RunningAppDiscoveryService.cs`
- `RunningAppDiscoveryService(...)` stores the exclusion list loader.
- `GetRunningApps(...)` returns the running desktop apps the user can close.
- `EnumerateBackgroundAppCandidates(...)` adds apps without visible windows when they still look like desktop apps.
- `EnumerateCandidateWindows(...)` gathers candidate windows and maps them to process IDs.
- `IsCandidateWindow(...)` filters out shell, cloaked, tool, and empty-title windows.
- `LooksLikeDesktopApp(...)` filters out processes that do not look like real interactive apps.
- `HasUiModuleLoaded(...)` checks whether a process has user32 or shell32 loaded.
- `TryGetVersionInfo(...)` safely reads file version metadata.
- `IsSupportedExecutablePath(...)` rejects missing executables and Windows-directory system executables.
- `IsExcludedProcessName(...)` checks the built-in exclusion list.
- `IsCustomExcludedApp(...)` applies the custom exclusion rules by name, path, company, and product metadata.
- `FirstNonEmpty(...)` returns the first non-blank string from a list.
- `ContainsAny(...)` checks whether a string contains one of several keywords.
- `IsExecutableNameExcluded(...)` checks the user-editable exclusion list.
- `NormalizeExecutableName(...)` normalizes raw inputs to a lower-cased `.exe` file name.
- `EnsureTrailingSeparator(...)` helps with prefix path checks.
- `IsWindowCloaked(...)` rejects cloaked DWM windows.
- `GetWindowTitle(...)` reads a window title string.
- `GetClassName(...)` reads a window class name.
- `WindowCandidate` stores the process ID and title for one discovered window.
- `ExcludedProcessNames`, `CustomExcludedProcessNames`, `CustomExcludedCompanyKeywords`, `CustomExcludedProductKeywords`, and `CustomExcludedPathKeywords` are the built-in exclusion sets.
- The P/Invoke declarations at the bottom provide the Win32 window and DWM calls used by discovery.

### `ShutdownScheduler.Core/Services/RemainingTimeFormatter.cs`
- `FormatHoursMinutesSeconds(...)` formats a countdown as `HH:MM:SS`.
- `FormatMinutesSeconds(...)` formats a short countdown as `MM:SS`.

### `ShutdownScheduler.Core/Services/LocalizationService.cs`
- `LocalizationService()` picks English or Spanish based on the current UI culture.
- The string indexer returns a localized string or falls back to English or the key itself.
- `T(...)` is a convenience alias for the string indexer.
- `ActionLabel(...)` returns a localized action name.
- `ScheduleModeLabel(...)` returns a localized schedule-mode name.
- `ExecutionModeLabel(...)` returns a localized execution-mode name.
- `StatusLabel(...)` returns a localized status label.
- `WeekdayLabel(...)` returns a localized weekday label.
- `English` and `Spanish` contain the translation tables used by the app.

### `ShutdownScheduler.Core/Services/MetricServices.cs`
- `CpuMonitorService()` tries to open the total CPU performance counter and primes it.
- `IsAvailable` reports whether CPU sampling is available.
- `SampleAverage()` returns a 60-second average total CPU usage.
- `SampleAverageForProcess(...)` returns a 60-second average for a single process name.
- `AddSample(...)` keeps the rolling CPU sample window trimmed.
- `Dispose()` releases the performance counter.
- `ProcessCpuSampler(...)` tracks per-process CPU snapshots.
- `ProcessCpuSampler.SampleAverage()` calculates the process-scoped CPU percentage and trims missing processes.
- `ProcessCpuSampler.AddSample(...)` keeps the rolling per-process sample window trimmed.
- `NetworkMonitorService()` uses rolling network-throughput samples.
- `SampleAverageBytesPerSecond()` returns a 60-second average for total network usage.
- `SampleAverageBytesPerSecondForProcess(...)` returns a 60-second average for one process name.
- `GetTotalBytes()` totals bytes received and sent across active adapters.
- `AddSample(...)` keeps the rolling network sample window trimmed.
- `ProcessNetworkSampler(...)` tracks per-process I/O counters.
- `ProcessNetworkSampler.SampleAverageBytesPerSecond()` calculates a rolling per-process byte-rate estimate.
- `ProcessNetworkSampler.AddSample(...)` keeps the rolling per-process network sample window trimmed.
- `BatteryMonitorService.IsBatteryAvailable` reports whether battery data can be read.
- `TryGetBatteryPercentage(...)` reads the current battery percentage through `GetSystemPowerStatus`.
- `ConditionEvaluationService(...)` wires together the monitor services and localization.
- `Evaluate(...)` checks the current request against CPU, network, and battery thresholds and returns a message if conditions are not met.
- `FormatBytes(...)` formats byte rates as KB/s or MB/s.

### `ShutdownScheduler.Core/Services/IWidgetService.cs`
- `Settings` exposes the widget settings object.
- `ApplySettings()` reapplies or persists widget settings.
- `Update(...)` pushes the current active schedule into the widget layer.

### `ShutdownScheduler.Core/Services/IUserNotificationService.cs`
- `ShowWarning(...)` displays a warning to the user.

### `ShutdownScheduler.Core/Services/IAppTimer.cs`
- `Interval` gets or sets the timer interval.
- `Tick` exposes the timer event.
- `Start()` starts the timer.
- `Stop()` stops the timer.
- `IAppTimerFactory.Create(...)` creates a timer instance for a given interval.

### `ShutdownScheduler.Core/Persistence/WidgetSettingsStore.cs`
- `Load()` reads widget settings from `%LOCALAPPDATA%/ShutdownScheduler/widget-settings.json`.
- `Save(...)` writes widget settings back to disk.

### `ShutdownScheduler.Core/Persistence/AppDetectionExclusionStore.cs`
- `LoadExecutableNames()` loads and normalizes the executable exclusion list.
- `PathOnDisk` exposes the actual file path used on disk.
- `EnsureFileExists()` creates the exclusion file with defaults if it is missing.
- `BuildDefaultFileContents()` generates the starter text file contents.
- `TryNormalizeExecutableName(...)` converts a raw line into a normalized `.exe` name.
- `DefaultExecutableNames` is the built-in list of executable names to ignore.

### `ShutdownScheduler.Core/Persistence/ExcludedExecutables.txt`
- Stores the default and user-editable executable names to ignore when building the close-app picker.
- The top comments explain the file format.

### `ShutdownScheduler.Core/ViewModels` and service helpers
- `ObservableObject`, `MainViewModel`, and the services above make up the shared app logic layer.
- Nothing in this project references WinUI, WPF, or Windows Forms directly.

## Legacy WPF-Era Files Kept as References

These files are still present in the repo, but the WinUI project excludes them from compilation. They are useful as behavior references and migration history.

### `Shutdown Scheduler.csproj`
- Legacy WPF project file.
- Targets `net10.0-windows`.
- Enables WPF and Windows Forms.
- References `ShutdownScheduler.Core`.
- Excludes the core project source from the legacy shell build.

### `Services/WidgetService.cs`
- `WidgetService(...)` loads widget settings and manages the legacy WPF widget window.
- `Settings` exposes the widget settings.
- `ApplySettings()` sizes, positions, saves, shows, or closes the WPF widget.
- `Update(...)` updates the WPF widget text from the active schedule.
- `EnsureWindow()` creates the widget window on demand.
- `ApplyGeometry()` positions the widget based on the selected anchor and screen DPI.
- `ApplyPresetSize()` maps the selected preset to concrete dimensions.
- `ClampToScreens()` keeps the widget on-screen.
- `GetWorkingBoundsInDip()` converts screen bounds from pixels to WPF DIPs.

### `Services/TrayService.cs`
- `TrayService(...)` creates the legacy Windows Forms tray icon and menu.
- `OpenRequested`, `CancelRequested`, and `ExitRequested` expose tray menu actions.
- `Show(...)` displays the tray icon and rebuilds the context menu.
- `Hide()` hides the tray icon.
- `RebuildMenu(...)` rebuilds the tray menu for active or inactive schedule state.
- `Dispose()` removes and disposes the tray icon.

### `Services/WpfAppTimer.cs`
- `WpfAppTimerFactory.Create(...)` creates a WPF `DispatcherTimer` wrapper.
- `WpfAppTimer(...)` wires the timer tick relay.
- `Interval`, `Tick`, `Start()`, and `Stop()` mirror the shared timer interface.

### `Services/WpfUserNotificationService.cs`
- `ShowWarning(...)` shows a standard WPF message box warning.

### `Views/WarningWindow.xaml` and `Views/WarningWindow.xaml.cs`
- These are the legacy warning-dialog pair described above.
- The code-behind exposes the button handlers and the result flags.

### `Properties/launchSettings.json`, `README.md`, and migration docs
- These files document how the app is run, built, and migrated.
- They do not contain application logic.

## Assets

### `Assets/AppIcon.ico`
- Native icon used by the WinUI window and tray icon helper.

### `Assets/IconPreview.png`
- Preview image generated by the icon tool.

### `Assets/LockScreenLogo.scale-200.png`
- Packaged lock-screen logo asset.

### `Assets/SplashScreen.scale-200.png`
- Packaged splash-screen asset.

### `Assets/Square150x150Logo.scale-200.png`
- Packaged tile/logo asset.

### `Assets/Square44x44Logo.scale-200.png`
- Packaged small tile/logo asset.

### `Assets/Square44x44Logo.targetsize-24_altform-unplated.png`
- Alternate small logo variant for unplated surfaces.

### `Assets/StoreLogo.png`
- Store listing logo asset.

### `Assets/Wide310x150Logo.scale-200.png`
- Wide tile asset used by the app manifest.

## Icon Generation Tool

### `Assets/AppIcon.svg`
- Vector master of the app icon: a power symbol whose stem is a clock hand, on a violet-to-coral squircle.

### `tools/generate_app_icons.py`
- Renders `AppIcon.svg` with headless Chrome and Pillow into every PNG in `Assets/`, plus the multi-size `AppIcon.ico`.
- Run `python tools/generate_app_icons.py` after editing the SVG.

## Solution Files

### `Shutdown Scheduler WinUI.slnx`
- Solution for the active WinUI app and core project.

### `Shutdown Scheduler.slnx`
- Older solution pairing the legacy WPF shell with the shared core project.
