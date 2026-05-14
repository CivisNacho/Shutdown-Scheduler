using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Services;
using Shutdown_Scheduler.ViewModels;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace Shutdown_Scheduler_WinUI
{
    public sealed partial class MainWindow : Window
    {
        private const int MinimumWindowWidth = 1366;
        private const int MinimumWindowHeight = 768;
        private readonly SchedulingService _scheduler;
        private readonly LocalizationService _loc;
        private readonly NativeMethods.SubclassProc _subclassProc;
        private nint _windowHandle;
        private bool _syncingPickerValues;
        private AppWindow? _appWindow;

        public MainWindow(MainViewModel viewModel, SchedulingService scheduler, LocalizationService loc)
        {
            ViewModel = viewModel;
            _scheduler = scheduler;
            _loc = loc;
            _subclassProc = WindowSubclassProc;
            InitializeComponent();
            ConfigureWindowSize();
            Root.DataContext = ViewModel;
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
        }

        private void ConfigureWindowSize()
        {
            _windowHandle = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Resize(new SizeInt32(MinimumWindowWidth, MinimumWindowHeight));

            NativeMethods.SetWindowSubclass(_windowHandle, _subclassProc, 1, 0);
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            Root.ActualThemeChanged -= OnRootActualThemeChanged;

            if (_windowHandle != 0)
            {
                NativeMethods.RemoveWindowSubclass(_windowHandle, _subclassProc, 1);
            }
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
                minMaxInfo.MinTrackSize.X = MinimumWindowWidth;
                minMaxInfo.MinTrackSize.Y = MinimumWindowHeight;
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
            if (_syncingPickerValues || double.IsNaN(args.NewValue) || args.NewValue <= 0)
            {
                return;
            }

            ViewModel.RelativeMinutes = args.NewValue.ToString(CultureInfo.CurrentCulture);
            SyncPickersFromViewModel();
        }

        private void OnTargetTimeChanged(object sender, TimePickerValueChangedEventArgs args)
        {
            SyncViewModelFromPickers();
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
                    Background = Brushes.Transparent,
                    Content = optionsPanel
                },
                CloseButtonText = _loc.T("CancelButton"),
                XamlRoot = Root.XamlRoot,
                Style = ResolveStyle("ThemedContentDialogStyle")
            };

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

            void AddConditionOption(StackPanel panel, string label, System.Windows.Input.ICommand command, Func<bool> isEnabled)
            {
                var button = new Button
                {
                    Content = label,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsEnabled = isEnabled(),
                    Style = ResolveStyle("ConditionDialogOptionButtonStyle")
                };

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
                    XamlRoot = Root.XamlRoot,
                    Style = ResolveStyle("ThemedContentDialogStyle")
                };

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
                    XamlRoot = Root.XamlRoot,
                    Style = ResolveStyle("ThemedContentDialogStyle")
                };

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

        private static Style? ResolveStyle(string resourceKey)
        {
            return Application.Current.Resources.TryGetValue(resourceKey, out var styleResource) && styleResource is Style style
                ? style
                : null;
        }

        private static partial class NativeMethods
        {
            public const uint WmGetMinMaxInfo = 0x0024;

            public delegate nint SubclassProc(nint hWnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);

            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint subclassId, nuint referenceData);

            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint subclassId);

            [DllImport("comctl32.dll", SetLastError = true)]
            public static extern nint DefSubclassProc(nint hWnd, uint message, nuint wParam, nint lParam);

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
