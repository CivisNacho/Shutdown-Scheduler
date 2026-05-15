using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Services;
using Shutdown_Scheduler.ViewModels;
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
using System.Runtime.InteropServices.WindowsRuntime;

namespace Shutdown_Scheduler_WinUI
{
    public sealed partial class MainWindow : Window
    {
        private const int FixedWindowWidth = 1600;
        private const int FixedWindowHeight = 760;
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
            CloseAppPickerPanel.DataContext = _closeAppPicker;
            _closeAppPicker.SelectedItemChanged += OnCloseAppPickerSelectionChanged;
            Root.Loaded += OnLoaded;
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
            if (!_hasWarmedCloseAppPicker)
            {
                _hasWarmedCloseAppPicker = true;
                _ = RefreshCloseAppTargetsAsync();
            }
        }

        private void ConfigureWindowSize()
        {
            _windowHandle = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Closing += OnAppWindowClosing;
            _appWindow.Resize(new SizeInt32(FixedWindowWidth, FixedWindowHeight));
            ApplyWindowIcon();

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }

            NativeMethods.RemoveMaximizeButton(_windowHandle);
            NativeMethods.SetWindowSubclass(_windowHandle, _subclassProc, 1, 0);
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            _closeAppPicker.SelectedItemChanged -= OnCloseAppPickerSelectionChanged;
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
            var bg = ResolveThemeColor("AppChromeBackgroundBrush", Color.FromArgb(255, 11, 11, 12));
            var fg = ResolveThemeColor("HeroValueBrush", Colors.White);
            var muted = ResolveThemeColor("MutedTextBrush", Color.FromArgb(255, 139, 134, 126));
            var hover = ResolveThemeColor("GlassPanelSecondaryBackgroundBrush", Color.FromArgb(255, 24, 25, 28));
            var pressed = ResolveThemeColor("GlassPanelStrongBorderBrush", Color.FromArgb(255, 90, 100, 112));

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

        private static Color ResolveThemeColor(string resourceKey, Color fallback)
        {
            if (TryResolveBrush(Application.Current.Resources, resourceKey, out var brush))
            {
                return brush.Color;
            }

            return fallback;
        }

        private static bool TryResolveBrush(ResourceDictionary dictionary, string resourceKey, out SolidColorBrush brush)
        {
            if (dictionary.TryGetValue(resourceKey, out var resource) &&
                resource is SolidColorBrush directBrush)
            {
                brush = directBrush;
                return true;
            }

            foreach (var mergedDictionary in dictionary.MergedDictionaries)
            {
                if (TryResolveBrush(mergedDictionary, resourceKey, out brush))
                {
                    return true;
                }
            }

            brush = null!;
            return false;
        }

        private nint WindowSubclassProc(nint hWnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
        {
            if (message == NativeMethods.WmGetMinMaxInfo)
            {
                var minMaxInfo = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
                minMaxInfo.MinTrackSize.X = FixedWindowWidth;
                minMaxInfo.MinTrackSize.Y = FixedWindowHeight;
                minMaxInfo.MaxTrackSize.X = FixedWindowWidth;
                minMaxInfo.MaxTrackSize.Y = FixedWindowHeight;
                Marshal.StructureToPtr(minMaxInfo, lParam, false);
                return 0;
            }

            return NativeMethods.DefSubclassProc(hWnd, message, wParam, lParam);
        }

        private void OnTargetDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            SyncViewModelFromPickers();
        }

        private void OnRelativeMinutesValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_syncingPickerValues || double.IsNaN(args.NewValue))
            {
                return;
            }

            ViewModel.RelativeMinutes = Math.Max(1, args.NewValue).ToString(CultureInfo.CurrentCulture);
            SyncPickersFromViewModel();
        }

        private void OnTargetTimeChanged(object sender, TimePickerValueChangedEventArgs args)
        {
            SyncViewModelFromPickers();
        }

        private async void OnRefreshCloseAppTargetsClick(object sender, RoutedEventArgs e)
        {
            await RefreshCloseAppTargetsAsync();
        }

        private async void OnAddConditionClick(object sender, RoutedEventArgs e)
        {
            var optionsPanel = new StackPanel
            {
                MinWidth = 320,
                Spacing = 8
            };
            var dialog = new ContentDialog
            {
                Title = ViewModel.AddConditionDialogTitle,
                Content = new ScrollViewer
                {
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(Colors.Transparent),
                    Content = optionsPanel
                },
                CloseButtonText = _loc.T("CancelButton"),
                XamlRoot = Root.XamlRoot
            };
            ApplyDialogColors(dialog);

            AddConditionOption(
                optionsPanel,
                _loc.T("NetworkBelow"),
                ViewModel.AddNetworkConditionCommand,
                () => ViewModel.CanAddNetworkCondition);

            AddConditionOption(
                optionsPanel,
                _loc.T("CpuBelow"),
                ViewModel.AddCpuConditionCommand,
                () => ViewModel.CanAddCpuCondition);

            if (ViewModel.IsBatteryConditionAvailable)
            {
                AddConditionOption(
                    optionsPanel,
                    _loc.T("BatteryBelow"),
                    ViewModel.AddBatteryConditionCommand,
                    () => ViewModel.CanAddBatteryCondition);
            }

            void AddConditionOption(StackPanel panel, string label, System.Windows.Input.ICommand command, Func<bool> isEnabled)
            {
                var button = new Button
                {
                    Content = label,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    MinHeight = 42,
                    Padding = new Thickness(16, 10, 16, 10),
                    IsEnabled = isEnabled()
                };
                ApplyDialogButtonColors(button);

                button.Click += (_, _) =>
                {
                    if (command.CanExecute(null))
                    {
                        command.Execute(null);
                    }

                    dialog.Hide();
                };

                panel.Children.Add(button);
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
                var target = ViewModel.SelectedTargetDateTime ?? DateTime.Now.AddMinutes(45);
                TargetDatePicker.MinDate = new DateTimeOffset(DateTime.Today);
                TargetDatePicker.MaxDate = new DateTimeOffset(DateTime.Today.AddDays(7));
                TargetDatePicker.Date = new DateTimeOffset(target.Date);
                TargetTimePicker.Time = target.TimeOfDay;

                if (double.TryParse(ViewModel.RelativeMinutes, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes))
                {
                    RelativeMinutesNumberBox.Value = minutes;
                }
            }
            finally
            {
                _syncingPickerValues = false;
            }
        }

        private void SyncViewModelFromPickers()
        {
            if (_syncingPickerValues)
            {
                return;
            }

            if (TargetDatePicker.Date is not { } selectedDate)
            {
                return;
            }

            var date = selectedDate.Date;
            var time = TargetTimePicker.Time;
            ViewModel.SelectedTargetDateTime = date + time;
        }

        private void OnSchedulerError(object? sender, string message)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = _loc.T("WarningTitle"),
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = Root.XamlRoot
                };
                ApplyDialogColors(dialog);

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
                    CloseButtonText = "OK",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Root.XamlRoot
                };
                ApplyDialogColors(dialog);

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

        private static void ApplyDialogColors(ContentDialog dialog)
        {
            dialog.Background = ResolveThemeBrush("PopupBackgroundBrush", new SolidColorBrush(Color.FromArgb(255, 24, 25, 28)));
            dialog.BorderBrush = ResolveThemeBrush("PopupBorderBrush", new SolidColorBrush(Color.FromArgb(255, 53, 57, 64)));
            dialog.Foreground = ResolveThemeBrush("ControlForegroundBrush", new SolidColorBrush(Colors.White));
            dialog.BorderThickness = new Thickness(1);
        }

        private static void ApplyDialogButtonColors(Button button)
        {
            var normalBackground = ResolveThemeBrush("PopupSelectedBrush", new SolidColorBrush(Color.FromArgb(255, 43, 49, 56)));
            var hoverBackground = normalBackground;
            var pressedBackground = ResolveThemeBrush("PopupPressedBrush", new SolidColorBrush(Color.FromArgb(255, 49, 56, 65)));
            var borderBrush = ResolveThemeBrush("PopupBorderBrush", new SolidColorBrush(Color.FromArgb(255, 53, 57, 64)));
            var foreground = ResolveThemeBrush("ControlForegroundBrush", new SolidColorBrush(Colors.White));

            button.Background = normalBackground;
            button.BorderBrush = borderBrush;
            button.Foreground = foreground;
            button.BorderThickness = new Thickness(1);
            button.Resources["ButtonBackgroundPointerOver"] = hoverBackground;
            button.Resources["ButtonBackgroundPressed"] = pressedBackground;
            button.Resources["ButtonBackgroundDisabled"] = normalBackground;
            button.Resources["ButtonBorderBrushPointerOver"] = borderBrush;
            button.Resources["ButtonBorderBrushPressed"] = borderBrush;
            button.Resources["ButtonBorderBrushDisabled"] = borderBrush;
            button.Resources["ButtonForegroundPointerOver"] = foreground;
            button.Resources["ButtonForegroundPressed"] = foreground;
        }

        private static Brush ResolveThemeBrush(string resourceKey, Brush fallback)
        {
            return TryResolveBrush(Application.Current.Resources, resourceKey, out var brush)
                ? brush
                : fallback;
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

        private static partial class NativeMethods
        {
            public const uint WmGetMinMaxInfo = 0x0024;
            public const uint WmSetIcon = 0x0080;
            public const int IconSmall = 0;
            public const int IconBig = 1;
            private const int GwlStyle = -16;
            private const nint WsMaximizeBox = 0x00010000;
            private const uint SwpNoSize = 0x0001;
            private const uint SwpNoMove = 0x0002;
            private const uint SwpNoZOrder = 0x0004;
            private const uint SwpFrameChanged = 0x0020;

            public delegate nint SubclassProc(nint hWnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);

            public static void RemoveMaximizeButton(nint hWnd)
            {
                var style = GetWindowLongPtr(hWnd, GwlStyle);
                SetWindowLongPtr(hWnd, GwlStyle, style & ~WsMaximizeBox);
                SetWindowPos(hWnd, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
            }

            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint subclassId, nuint referenceData);

            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint subclassId);

            [DllImport("comctl32.dll", SetLastError = true)]
            public static extern nint DefSubclassProc(nint hWnd, uint message, nuint wParam, nint lParam);

            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
            private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

            [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
            private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

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
        }
    }
}
