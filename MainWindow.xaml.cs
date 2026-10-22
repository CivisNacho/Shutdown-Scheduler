using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Persistence;
using Shutdown_Scheduler.Services;
using Shutdown_Scheduler.ViewModels;
using Shutdown_Scheduler_WinUI.Helpers;
using Shutdown_Scheduler_WinUI.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation;
using System.Runtime.InteropServices.WindowsRuntime;
using System.ComponentModel;
using Windows.Globalization.DateTimeFormatting;

namespace Shutdown_Scheduler_WinUI
{
    public sealed partial class MainWindow : Window
    {
        // Window sizes are in DIPs so the layout breakpoints behave the same at any display scaling.
        private const int DefaultWindowWidthDip = 1240;
        private const int DefaultWindowHeightDip = 700;
        private const int MinWindowWidthDip = 520;
        private const int MinWindowHeightDip = 560;
        private const int WindowChromePadding = 24;
        private const double WideLayoutMinWidth = 1100;
        private const double MediumLayoutMinWidth = 760;
        private const double StackedConditionsMaxHeight = 420;
        private readonly WindowPlacementStore _placementStore = new();
        private readonly SchedulingService _scheduler;
        private readonly LocalizationService _loc;
        private readonly AppSession _session;
        private readonly RunningAppDiscoveryService _runningApps;
        private readonly WinUiAppIconCache _appIconCache;
        private readonly CloseAppPickerState _closeAppPicker;
        private readonly SemaphoreSlim _iconLoadGate = new(2, 2);
        private readonly NativeMethods.SubclassProc _subclassProc;
        private nint _windowHandle;
        private nint _smallWindowIcon;
        private nint _largeWindowIcon;
        private bool _syncingPickerValues;
        private bool _hasWarmedCloseAppPicker;
        private bool _lastKnownHasActiveSchedule;
        private bool _restoreMaximized;
        private LayoutMode? _layoutMode;
        private CancellationTokenSource? _closeAppPickerCts;
        private AppWindow? _appWindow;

        public MainWindow(
            MainViewModel viewModel,
            SchedulingService scheduler,
            LocalizationService loc,
            AppSession session,
            RunningAppDiscoveryService runningApps,
            WinUiAppIconCache appIconCache)
        {
            ViewModel = viewModel;
            _scheduler = scheduler;
            _loc = loc;
            _session = session;
            _runningApps = runningApps;
            _appIconCache = appIconCache;
            _closeAppPicker = new CloseAppPickerState(loc);
            _subclassProc = WindowSubclassProc;
            InitializeComponent();
            ConfigureWindowSize();
            Root.DataContext = ViewModel;
            Title = string.Empty;
            CloseAppPickerPanel.DataContext = _closeAppPicker;
            _closeAppPicker.SelectedItemChanged += OnCloseAppPickerSelectionChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            Root.Loaded += OnLoaded;
            HoverTransitionFix.Attach(Root);
            Root.SizeChanged += OnRootSizeChanged;
            TargetDial.TargetChanged += OnTargetDialChanged;
            Root.ActualThemeChanged += OnRootActualThemeChanged;
            _scheduler.WarningDue += OnWarningDue;
            _scheduler.Error += OnSchedulerError;
            Closed += OnClosed;
        }

        public MainViewModel ViewModel { get; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SyncPickersFromViewModel();
            ApplyTitleBarTheme();
            if (_restoreMaximized)
            {
                // Maximizing before the window is shown would flash it at the restored size first.
                _restoreMaximized = false;
                (_appWindow?.Presenter as OverlappedPresenter)?.Maximize();
            }

            _lastKnownHasActiveSchedule = ViewModel.HasActiveSchedule;
            // Templates are applied lazily, so strip once they have settled.
            Root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => HoverTransitionFix.StripAll(Root));
            if (!_hasWarmedCloseAppPicker)
            {
                _hasWarmedCloseAppPicker = true;
                _ = RefreshCloseAppTargetsAsync();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.HasActiveSchedule))
            {
                return;
            }

            if (ViewModel.HasActiveSchedule == _lastKnownHasActiveSchedule)
            {
                return;
            }

            _lastKnownHasActiveSchedule = ViewModel.HasActiveSchedule;
            AnnounceForAccessibility(ViewModel.HasActiveSchedule
                ? "Schedule active."
                : "Schedule canceled.");
        }

        private void ConfigureWindowSize()
        {
            _windowHandle = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Closing += OnAppWindowClosing;

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
            }

            NativeMethods.SetWindowSubclass(_windowHandle, _subclassProc, 1, 0);
            if (!TryRestoreWindowPlacement())
            {
                ApplyDefaultWindowBounds(windowId);
            }

            ApplyWindowIcon();
        }

        private enum LayoutMode
        {
            Narrow,
            Medium,
            Wide
        }

        private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Width);

        // VisualStateManager triggers are never evaluated for a desktop Window's content (it is not a Control),
        // so the one/two/three-column breakpoints are applied here.
        private void ApplyLayout(double width)
        {
            var mode = width >= WideLayoutMinWidth ? LayoutMode.Wide
                : width >= MediumLayoutMinWidth ? LayoutMode.Medium
                : LayoutMode.Narrow;
            if (mode == _layoutMode)
            {
                return;
            }

            _layoutMode = mode;
            var star = new GridLength(1, GridUnitType.Star);
            var auto = GridLength.Auto;

            // Only create the tracks in use: Grid spacing is also added around zero-size tracks,
            // which would leave an uneven gap on the right and bottom.
            MainSectionsGrid.ColumnDefinitions.Clear();
            MainSectionsGrid.RowDefinitions.Clear();
            var columnCount = mode switch { LayoutMode.Wide => 3, LayoutMode.Medium => 2, _ => 1 };
            var rowCount = mode switch { LayoutMode.Wide => 1, LayoutMode.Medium => 2, _ => 3 };
            for (var i = 0; i < columnCount; i++)
            {
                MainSectionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = star });
            }

            for (var i = 0; i < rowCount; i++)
            {
                MainSectionsGrid.RowDefinitions.Add(new RowDefinition { Height = mode == LayoutMode.Wide ? star : auto });
            }

            // Narrow: schedule, conditions, widget stacked. Medium: schedule | conditions, widget below both.
            Grid.SetColumn(SectionRegion2, mode == LayoutMode.Narrow ? 0 : 1);
            Grid.SetRow(SectionRegion2, mode == LayoutMode.Narrow ? 1 : 0);
            Grid.SetColumn(SectionRegion3, mode == LayoutMode.Wide ? 2 : 0);
            Grid.SetRow(SectionRegion3, mode switch { LayoutMode.Wide => 0, LayoutMode.Medium => 1, _ => 2 });
            Grid.SetColumnSpan(SectionRegion3, mode == LayoutMode.Medium ? 2 : 1);

            // Wide sections fill the height and scroll individually; stacked ones scroll as a whole page,
            // so the conditions list needs a finite height to keep its own scrolling.
            var stacked = mode != LayoutMode.Wide;
            SectionsScroller.VerticalScrollMode = stacked ? ScrollMode.Enabled : ScrollMode.Disabled;
            SectionsScroller.VerticalScrollBarVisibility = stacked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            ConditionsList.MaxHeight = stacked ? StackedConditionsMaxHeight : double.PositiveInfinity;

            var narrow = mode == LayoutMode.Narrow;
            Grid.SetRow(HeaderActionsHost, narrow ? 1 : 0);
            Grid.SetColumn(HeaderActionsHost, narrow ? 0 : 1);
            Grid.SetColumnSpan(HeaderActionsHost, narrow ? 2 : 1);
            HeaderActionsHost.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
            HeaderActionsHost.ColumnDefinitions[0].Width = narrow ? star : auto;
            HeaderActionsHost.ColumnDefinitions[1].Width = narrow ? star : auto;
            ScheduleButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            CancelScheduleButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        }

        private int ToPixels(int dip)
            => (int)Math.Round(dip * NativeMethods.GetDpiForWindow(_windowHandle) / 96.0);

        private void ApplyDefaultWindowBounds(Microsoft.UI.WindowId windowId)
        {
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
            var workArea = displayArea.WorkArea;

            var width = Math.Min(ToPixels(DefaultWindowWidthDip), Math.Max(1, workArea.Width - WindowChromePadding));
            var height = Math.Min(ToPixels(DefaultWindowHeightDip), Math.Max(1, workArea.Height - WindowChromePadding));
            var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);

            _appWindow?.MoveAndResize(new RectInt32(x, y, width, height));
        }

        private bool TryRestoreWindowPlacement()
        {
            var saved = _placementStore.Load();
            if (_appWindow is null || saved is null || saved.Width <= 0 || saved.Height <= 0)
            {
                return false;
            }

            // Only restore onto a monitor that still exists: the title bar must land inside some work area.
            var titleBarPoint = new PointInt32(saved.X + saved.Width / 2, saved.Y + 16);
            var displayArea = DisplayArea.GetFromPoint(titleBarPoint, DisplayAreaFallback.None);
            if (displayArea is null)
            {
                return false;
            }

            var workArea = displayArea.WorkArea;
            var width = Math.Min(saved.Width, workArea.Width);
            var height = Math.Min(saved.Height, workArea.Height);
            var x = Math.Clamp(saved.X, workArea.X, workArea.X + workArea.Width - width);
            var y = Math.Clamp(saved.Y, workArea.Y, workArea.Y + workArea.Height - height);

            _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
            _restoreMaximized = saved.IsMaximized;
            return true;
        }

        private void SaveWindowPlacement()
        {
            if (_appWindow?.Presenter is not OverlappedPresenter presenter ||
                presenter.State == OverlappedPresenterState.Minimized)
            {
                return;
            }

            var isMaximized = presenter.State == OverlappedPresenterState.Maximized;
            var bounds = new RectInt32(_appWindow.Position.X, _appWindow.Position.Y, _appWindow.Size.Width, _appWindow.Size.Height);
            if (isMaximized && NativeMethods.TryGetRestoredBounds(_windowHandle, out var restored))
            {
                // rcNormalPosition is relative to the work area, not the screen.
                var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
                restored.X += displayArea.WorkArea.X - displayArea.OuterBounds.X;
                restored.Y += displayArea.WorkArea.Y - displayArea.OuterBounds.Y;
                bounds = restored;
            }

            _placementStore.Save(new WindowPlacement
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                IsMaximized = isMaximized
            });
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            _closeAppPicker.SelectedItemChanged -= OnCloseAppPickerSelectionChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            Root.SizeChanged -= OnRootSizeChanged;
            Root.ActualThemeChanged -= OnRootActualThemeChanged;
            _scheduler.WarningDue -= OnWarningDue;
            _scheduler.Error -= OnSchedulerError;
            _closeAppPickerCts?.Cancel();
            _closeAppPickerCts?.Dispose();
            _iconLoadGate.Dispose();
            if (_appWindow is not null)
            {
                _appWindow.Closing -= OnAppWindowClosing;
            }

            if (_windowHandle != 0)
            {
                NativeMethods.RemoveWindowSubclass(_windowHandle, _subclassProc, 1);
            }

            AppIconAssets.DestroyIconHandle(_smallWindowIcon);
            AppIconAssets.DestroyIconHandle(_largeWindowIcon);
            _smallWindowIcon = nint.Zero;
            _largeWindowIcon = nint.Zero;
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            SaveWindowPlacement();
            if (_session.IsExiting || !ViewModel.HasActiveSchedule)
            {
                return;
            }

            _session.CloseMainWindowToTray(this);
        }

        public void BringToForeground()
        {
            NativeMethods.SetForegroundWindow(_windowHandle);
        }

        private void OnRootActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyTitleBarTheme();
        }

        private void ApplyTitleBarTheme()
        {
            if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var titleBar = _appWindow.TitleBar;
            var isLightTheme = Root.ActualTheme == ElementTheme.Light;

            var bg = ThemeResources.ResolveColor(
                "AppChromeBackgroundBrush",
                isLightTheme ? Color.FromArgb(255, 244, 241, 235) : Color.FromArgb(255, 11, 11, 12));
            var fg = ThemeResources.ResolveColor(
                "HeroValueBrush",
                isLightTheme ? Color.FromArgb(255, 29, 31, 34) : Colors.White);
            var muted = ThemeResources.ResolveColor(
                "MutedTextBrush",
                isLightTheme ? Color.FromArgb(255, 117, 109, 100) : Color.FromArgb(255, 139, 134, 126));
            var hover = ThemeResources.ResolveColor(
                "GlassPanelSecondaryBackgroundBrush",
                isLightTheme ? Color.FromArgb(255, 249, 246, 240) : Color.FromArgb(255, 24, 25, 28));
            var pressed = ThemeResources.ResolveColor(
                "GlassPanelStrongBorderBrush",
                isLightTheme ? Color.FromArgb(255, 130, 144, 161) : Color.FromArgb(255, 90, 100, 112));

            titleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
            titleBar.BackgroundColor = bg;
            titleBar.ForegroundColor = fg;
            titleBar.InactiveBackgroundColor = bg;
            titleBar.InactiveForegroundColor = muted;

            titleBar.ButtonBackgroundColor = bg;
            titleBar.ButtonForegroundColor = fg;
            titleBar.ButtonInactiveBackgroundColor = bg;
            titleBar.ButtonInactiveForegroundColor = muted;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = fg;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonPressedForegroundColor = fg;
        }

        private void ApplyWindowIcon()
        {
            AppIconAssets.DestroyIconHandle(_smallWindowIcon);
            AppIconAssets.DestroyIconHandle(_largeWindowIcon);

            _smallWindowIcon = AppIconAssets.LoadIconHandle(16);
            _largeWindowIcon = AppIconAssets.LoadIconHandle(32);

            if (_smallWindowIcon != nint.Zero)
            {
                NativeMethods.SendMessage(_windowHandle, NativeMethods.WmSetIcon, (nuint)NativeMethods.IconSmall, _smallWindowIcon);
            }

            if (_largeWindowIcon != nint.Zero)
            {
                NativeMethods.SendMessage(_windowHandle, NativeMethods.WmSetIcon, (nuint)NativeMethods.IconBig, _largeWindowIcon);
            }
        }

        private nint WindowSubclassProc(nint hWnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
        {
            if (message == NativeMethods.WmGetMinMaxInfo)
            {
                var minMaxInfo = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
                minMaxInfo.MinTrackSize.X = ToPixels(MinWindowWidthDip);
                minMaxInfo.MinTrackSize.Y = ToPixels(MinWindowHeightDip);
                Marshal.StructureToPtr(minMaxInfo, lParam, false);
                return 0;
            }

            if (message == NativeMethods.WmDpiChanged)
            {
                // Apply the rect Windows suggests so the window keeps its logical size on the new monitor.
                var suggested = Marshal.PtrToStructure<NativeMethods.Rect>(lParam);
                NativeMethods.MoveWindowTo(hWnd, suggested);
            }

            return NativeMethods.DefSubclassProc(hWnd, message, wParam, lParam);
        }

        private void OnTargetDialChanged(object? sender, DateTime target)
        {
            if (!_syncingPickerValues)
            {
                ViewModel.SelectedTargetDateTime = target;
            }
        }

        private async void OnRefreshCloseAppTargetsClick(object sender, RoutedEventArgs e)
        {
            await RefreshCloseAppTargetsAsync();
        }

        private async void OnAddConditionClick(object sender, RoutedEventArgs e)
        {
            var optionsPanel = new StackPanel
            {
                MinWidth = 400,
                Spacing = 8
            };
            optionsPanel.Children.Add(new TextBlock
            {
                Text = _loc.T("ConditionsSectionHint"),
                Style = (Style)Application.Current.Resources["SectionCaptionTextStyle"],
                Margin = new Thickness(0, -4, 0, 8)
            });

            var dialog = new ContentDialog
            {
                Title = ViewModel.AddConditionDialogTitle,
                Content = new ScrollViewer
                {
                    MaxHeight = 460,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(Colors.Transparent),
                    Content = optionsPanel
                },
                CloseButtonText = _loc.T("CancelButton"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Root.XamlRoot
            };
            AutomationProperties.SetName(dialog, ViewModel.AddConditionDialogTitle);
            ThemeResources.ApplyDialogStyle(dialog);

            AddConditionOption(
                ConditionType.Network,
                _loc.T("NetworkBelow"),
                _loc.T("NetworkConditionHint"),
                ViewModel.AddNetworkConditionCommand,
                ViewModel.CanAddNetworkCondition);

            AddConditionOption(
                ConditionType.Cpu,
                _loc.T("CpuBelow"),
                _loc.T("CpuConditionHint"),
                ViewModel.AddCpuConditionCommand,
                ViewModel.CanAddCpuCondition);

            if (ViewModel.IsBatteryConditionAvailable)
            {
                AddConditionOption(
                    ConditionType.Battery,
                    _loc.T("BatteryBelow"),
                    _loc.T("BatteryConditionHint"),
                    ViewModel.AddBatteryConditionCommand,
                    ViewModel.CanAddBatteryCondition);
            }

            AddConditionOption(
                ConditionType.AppIdle,
                _loc.T("AppIdleBelow"),
                _loc.T("AppIdleConditionHint"),
                ViewModel.AddAppIdleConditionCommand,
                ViewModel.CanAddAppIdleCondition);

            // One row per condition: icon tile, title and a short explanation. Conditions already added stay
            // listed (dimmed, with an "Added" badge) so the list doesn't change shape between openings.
            void AddConditionOption(ConditionType type, string label, string description, System.Windows.Input.ICommand command, bool canAdd)
            {
                var resources = Application.Current.Resources;
                var row = new Grid { ColumnSpacing = 14 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(new Border
                {
                    Style = (Style)resources["SectionIconTileStyle"],
                    Child = new FontIcon
                    {
                        Glyph = AppGlyphs.ForCondition(type),
                        Style = (Style)resources["SectionIconStyle"]
                    }
                });

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
                text.Children.Add(new TextBlock
                {
                    Text = label,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Style = (Style)resources["FormRowLabelStyle"]
                });
                text.Children.Add(new TextBlock
                {
                    Text = description,
                    Style = (Style)resources["SectionCaptionTextStyle"],
                    TextWrapping = TextWrapping.WrapWholeWords
                });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);

                if (!canAdd)
                {
                    var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                    badge.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 12, Style = (Style)resources["SectionIconStyle"] });
                    badge.Children.Add(new TextBlock { Text = _loc.T("ConditionAdded"), Style = (Style)resources["SectionCaptionTextStyle"] });
                    Grid.SetColumn(badge, 2);
                    row.Children.Add(badge);
                }

                var button = new Button
                {
                    Content = row,
                    IsEnabled = canAdd,
                    Opacity = canAdd ? 1 : 0.6,
                    Style = (Style)resources["DialogOptionButtonStyle"]
                };
                AutomationProperties.SetName(button, label);
                AutomationProperties.SetHelpText(button, description);

                button.Click += (_, _) =>
                {
                    if (command.CanExecute(null))
                    {
                        command.Execute(null);
                    }

                    dialog.Hide();
                };

                optionsPanel.Children.Add(button);
            }

            await dialog.ShowAsync();
        }

        private void OnRemoveConditionClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ConditionEditorItem condition } &&
                ViewModel.RemoveConditionCommand.CanExecute(condition))
            {
                ViewModel.RemoveConditionCommand.Execute(condition);
            }
        }

        private void SyncPickersFromViewModel()
        {
            _syncingPickerValues = true;
            try
            {
                TargetDial.Target = ViewModel.SelectedTargetDateTime ?? DateTime.Now.AddMinutes(45);
            }
            finally
            {
                _syncingPickerValues = false;
            }
        }

        private void OnSchedulerError(object? sender, string message)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = _loc.T("WarningTitle"),
                    Content = message,
                    CloseButtonText = _loc.T("Ok"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Root.XamlRoot
                };
                AutomationProperties.SetName(dialog, _loc.T("WarningTitle"));
                ThemeResources.ApplyDialogStyle(dialog);

                await dialog.ShowAsync();
            });
        }

        private void OnWarningDue(object? sender, ActiveSchedule schedule)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                var remaining = RemainingTimeFormatter.FormatMinutesSeconds(schedule.TargetTime - DateTime.Now);
                var dialog = new ContentDialog
                {
                    Title = _loc.T("WarningTitle"),
                    Content = string.Format(_loc.T("WarningText"), _loc.ActionLabel(schedule.Request.Action), remaining),
                    PrimaryButtonText = _loc.T("Postpone"),
                    SecondaryButtonText = _loc.T("CancelButton"),
                    CloseButtonText = _loc.T("Ok"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Root.XamlRoot
                };
                AutomationProperties.SetName(dialog, _loc.T("WarningTitle"));
                ThemeResources.ApplyDialogStyle(dialog);

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _scheduler.Postpone();
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    _scheduler.Cancel();
                }
            });
        }

        private void OnCloseAppPickerSelectionChanged(object? sender, CloseAppPickerItem? item)
        {
            ViewModel.SetSelectedAppTarget(item is null
                ? null
                : new AppTarget(item.ProcessName, item.Label, item.ExecutablePath));
        }

        private async Task RefreshCloseAppTargetsAsync()
        {
            _closeAppPickerCts?.Cancel();
            _closeAppPickerCts?.Dispose();
            var currentRefresh = new CancellationTokenSource();
            _closeAppPickerCts = currentRefresh;
            var cancellationToken = currentRefresh.Token;
            var previousProcessName = _closeAppPicker.SelectedItem?.ProcessName;
            var previousExecutablePath = _closeAppPicker.SelectedItem?.ExecutablePath;

            _closeAppPicker.IsLoading = true;

            try
            {
                var runningApps = await Task.Run(
                    () => _runningApps.GetRunningApps(Environment.ProcessId),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                ViewModel.SetRunningApps(runningApps);

                var pickerItems = runningApps
                    .Select(app => new CloseAppPickerItem(app.ProcessName, app.Label, app.ExecutablePath))
                    .ToList();

                _closeAppPicker.Items.Clear();
                foreach (var item in pickerItems)
                {
                    _closeAppPicker.Items.Add(item);
                }

                _closeAppPicker.SelectedItem = FindBestSelection(pickerItems, previousProcessName, previousExecutablePath)
                    ?? pickerItems.FirstOrDefault();

                _ = WarmIconsAsync(pickerItems, cancellationToken);
                AnnounceForAccessibility($"Found {pickerItems.Count} running apps.");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_closeAppPickerCts, currentRefresh))
                {
                    _closeAppPicker.IsLoading = false;
                }
            }
        }

        private async Task WarmIconsAsync(IReadOnlyList<CloseAppPickerItem> items, CancellationToken cancellationToken)
        {
            try
            {
                await Task.WhenAll(items.Select(item => LoadIconAsync(item, cancellationToken)));
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task LoadIconAsync(CloseAppPickerItem item, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(item.ExecutablePath))
            {
                return;
            }

            await _iconLoadGate.WaitAsync(cancellationToken);
            try
            {
                var iconBytes = await _appIconCache.GetOrLoadAsync(item.ExecutablePath, cancellationToken);
                if (iconBytes is null)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await RunOnUiThreadAsync(async () =>
                {
                    using var memory = new MemoryStream(iconBytes, writable: false);
                    var image = new BitmapImage();
                    await image.SetSourceAsync(memory.AsRandomAccessStream());
                    item.Icon = image;
                    item.IsIconLoaded = true;
                });
            }
            finally
            {
                _iconLoadGate.Release();
            }
        }

        private Task RunOnUiThreadAsync(Func<Task> action)
        {
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(() =>
                {
                    _ = RunAsync();

                    async Task RunAsync()
                    {
                        try
                        {
                            await action();
                            completion.SetResult(null);
                        }
                        catch (Exception ex)
                        {
                            completion.SetException(ex);
                        }
                    }
                }))
            {
                completion.SetException(new InvalidOperationException("Unable to queue UI update."));
            }

            return completion.Task;
        }

        private static CloseAppPickerItem? FindBestSelection(
            IEnumerable<CloseAppPickerItem> items,
            string? processName,
            string? executablePath)
        {
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var byProcessName = items.FirstOrDefault(item =>
                    string.Equals(item.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
                if (byProcessName is not null)
                {
                    return byProcessName;
                }
            }

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return items.FirstOrDefault(item =>
                    string.Equals(item.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private void AnnounceForAccessibility(string message)
        {
            AccessibilityStatusText.Text = $"{message} {DateTime.Now:HH:mm:ss}";
        }

        private static partial class NativeMethods
        {
            public const uint WmGetMinMaxInfo = 0x0024;
            public const uint WmSetIcon = 0x0080;
            public const uint WmDpiChanged = 0x02E0;
            public const int IconSmall = 0;
            public const int IconBig = 1;
            private const uint SwpNoZOrder = 0x0004;
            private const uint SwpNoActivate = 0x0010;

            public delegate nint SubclassProc(nint hWnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);

            public static void MoveWindowTo(nint hWnd, Rect rect)
            {
                SetWindowPos(hWnd, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, SwpNoZOrder | SwpNoActivate);
            }

            public static bool TryGetRestoredBounds(nint hWnd, out RectInt32 bounds)
            {
                var placement = new WindowPlacementInfo { Length = Marshal.SizeOf<WindowPlacementInfo>() };
                if (!GetWindowPlacement(hWnd, ref placement))
                {
                    bounds = default;
                    return false;
                }

                var r = placement.NormalPosition;
                bounds = new RectInt32(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                return true;
            }

            [DllImport("user32.dll")]
            public static extern uint GetDpiForWindow(nint hWnd);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetWindowPlacement(nint hWnd, ref WindowPlacementInfo placement);

            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint subclassId, nuint referenceData);

            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint subclassId);

            [DllImport("comctl32.dll", SetLastError = true)]
            public static extern nint DefSubclassProc(nint hWnd, uint message, nuint wParam, nint lParam);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(nint hWnd);

            [DllImport("user32.dll", EntryPoint = "SendMessageW")]
            public static extern nint SendMessage(nint hWnd, uint message, nuint wParam, nint lParam);

            [StructLayout(LayoutKind.Sequential)]
            public struct Point
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MinMaxInfo
            {
                public Point Reserved;
                public Point MaxSize;
                public Point MaxPosition;
                public Point MinTrackSize;
                public Point MaxTrackSize;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct WindowPlacementInfo
            {
                public int Length;
                public int Flags;
                public int ShowCmd;
                public Point MinPosition;
                public Point MaxPosition;
                public Rect NormalPosition;
            }
        }
    }
}
