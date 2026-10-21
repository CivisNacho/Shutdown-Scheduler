using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Shutdown_Scheduler_WinUI.Controls;

// Picks the schedule's target time on a 12-hour clock face. The arc runs from "now" to the target, so the same
// gesture shows both the clock time and how long until it. Dragging the knob past a full turn keeps going (+12 h),
// day chips move the date, presets set "now + N".
public sealed partial class TargetTimeDial : UserControl
{
    private const double Center = 98;
    private const double RingRadius = 86;
    private const double KnobRadius = 12;
    private const int DragStepMinutes = 5;
    private const int MaxDaysAhead = 7;
    private static readonly int[] Presets = [15, 30, 60, 120];

    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(DateTime), typeof(TargetTimeDial),
            new PropertyMetadata(DateTime.Now.AddMinutes(45), (d, e) => ((TargetTimeDial)d).OnTargetChanged((DateTime)e.OldValue, (DateTime)e.NewValue)));

    public static readonly DependencyProperty InLabelProperty = RegisterLabel(nameof(InLabel));
    public static readonly DependencyProperty TodayLabelProperty = RegisterLabel(nameof(TodayLabel));
    public static readonly DependencyProperty TomorrowLabelProperty = RegisterLabel(nameof(TomorrowLabel));
    public static readonly DependencyProperty MoreDaysLabelProperty = RegisterLabel(nameof(MoreDaysLabel));
    public static readonly DependencyProperty TimePassedLabelProperty = RegisterLabel(nameof(TimePassedLabel));

    private readonly List<(int Minutes, Button Button)> _presetButtons = new();
    private readonly List<(int DayOffset, Button Button)> _dayButtons = new();
    private Button? _calendarButton;
    private DispatcherQueueTimer? _clock;
    private bool _coercing;

    public TargetTimeDial()
    {
        InitializeComponent();
        BuildTicks();
        BuildPresets();
        BuildDays();

        DialSurface.PointerPressed += OnDialPointerPressed;
        DialSurface.PointerMoved += OnDialPointerMoved;
        DialSurface.PointerReleased += OnDialPointerReleased;
        DialSurface.PointerWheelChanged += OnDialPointerWheelChanged;
        KeyDown += OnKeyDown;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _clock?.Stop();

        Render();
    }

    public event EventHandler<DateTime>? TargetChanged;

    public DateTime Target
    {
        get => (DateTime)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public string InLabel { get => (string)GetValue(InLabelProperty); set => SetValue(InLabelProperty, value); }
    public string TodayLabel { get => (string)GetValue(TodayLabelProperty); set => SetValue(TodayLabelProperty, value); }
    public string TomorrowLabel { get => (string)GetValue(TomorrowLabelProperty); set => SetValue(TomorrowLabelProperty, value); }
    public string MoreDaysLabel { get => (string)GetValue(MoreDaysLabelProperty); set => SetValue(MoreDaysLabelProperty, value); }
    public string TimePassedLabel { get => (string)GetValue(TimePassedLabelProperty); set => SetValue(TimePassedLabelProperty, value); }

    // Earliest and latest targets the dial produces; matches the view model's 7-day scheduling window.
    private static DateTime MinimumTarget => TruncateToMinute(DateTime.Now).AddMinutes(1);
    private static DateTime MaximumTarget => DateTime.Today.AddDays(MaxDaysAhead + 1).AddMinutes(-1);

    protected override AutomationPeer OnCreateAutomationPeer() => new TargetTimeDialAutomationPeer(this);

    private static DependencyProperty RegisterLabel(string name)
        => DependencyProperty.Register(name, typeof(string), typeof(TargetTimeDial),
            new PropertyMetadata(string.Empty, (d, _) => ((TargetTimeDial)d).Render()));

    // Gestures go through here: clamps to the scheduling window. A target that later slips into the past
    // (the window was left open) is not moved silently; the readout says so and validation blocks it.
    private void SetTargetFromInput(DateTime value)
    {
        var minimum = MinimumTarget;
        var maximum = MaximumTarget;
        Target = value < minimum ? minimum : value > maximum ? maximum : TruncateToMinute(value);
    }

    private void OnTargetChanged(DateTime oldValue, DateTime newValue)
    {
        if (_coercing) return;

        var truncated = TruncateToMinute(newValue);
        if (truncated != newValue)
        {
            _coercing = true;
            Target = truncated;
            _coercing = false;
        }

        if (truncated == oldValue) return;

        Render();
        if (FrameworkElementAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, MinutesUntil(oldValue), MinutesUntil(truncated));
        }

        TargetChanged?.Invoke(this, truncated);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The "now" hand, the arc and "in 1 h 30 min" move with the clock even when the target doesn't.
        if (_clock is null)
        {
            _clock = DispatcherQueue.CreateTimer();
            _clock.Interval = TimeSpan.FromSeconds(20);
            _clock.Tick += (_, _) => Render();
        }

        _clock.Start();
        Render();
    }

    private void Render()
    {
        var now = DateTime.Now;
        var target = Target;
        var minutesUntil = (target - now).TotalMinutes;
        var nowAngle = ClockAngle(now);
        var targetAngle = ClockAngle(target);

        NowHand.Data = new LineGeometry { StartPoint = PointAt(nowAngle, RingRadius - 9), EndPoint = PointAt(nowAngle, RingRadius + 9) };

        var fullTurn = minutesUntil >= 12 * 60;
        FullArc.Visibility = fullTurn ? Visibility.Visible : Visibility.Collapsed;
        ProgressArc.Visibility = !fullTurn && minutesUntil > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ProgressArc.Visibility == Visibility.Visible)
        {
            ProgressArc.Data = BuildArc(nowAngle, minutesUntil / 2);
        }

        var knob = PointAt(targetAngle, RingRadius);
        KnobTransform.X = knob.X - KnobRadius;
        KnobTransform.Y = knob.Y - KnobRadius;

        // Split "7:32 PM" so the clock digits stay large and the AM/PM designator rides along small.
        var pattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
        var usesMeridiem = pattern.Contains('t');
        TimeText.Text = usesMeridiem ? target.ToString(pattern.Replace("tt", "").Replace("t", "").Trim()) : target.ToString(pattern);
        MeridiemText.Text = usesMeridiem ? target.ToString("tt") : string.Empty;
        MeridiemText.Visibility = usesMeridiem ? Visibility.Visible : Visibility.Collapsed;

        DurationText.Text = minutesUntil < 0 ? TimePassedLabel : $"{InLabel} {FormatDuration(minutesUntil)}".Trim();
        var dayLabel = DayLabel(target.Date);
        DayText.Text = target.Date == DateTime.Today ? string.Empty : dayLabel;
        DayText.Visibility = target.Date == DateTime.Today ? Visibility.Collapsed : Visibility.Visible;

        foreach (var (presetMinutes, button) in _presetButtons)
        {
            SetChipSelected(button, WholeMinutes(minutesUntil) == presetMinutes);
        }

        var dayOffset = (target.Date - DateTime.Today).Days;
        foreach (var (offset, button) in _dayButtons)
        {
            button.Content = DayLabel(DateTime.Today.AddDays(offset));
            SetChipSelected(button, offset == dayOffset);
        }

        if (_calendarButton is not null)
        {
            SetChipSelected(_calendarButton, dayOffset > _dayButtons.Count - 1);
            ToolTipService.SetToolTip(_calendarButton, MoreDaysLabel);
            AutomationProperties.SetName(_calendarButton, MoreDaysLabel);
        }
    }

    private string DayLabel(DateTime date)
    {
        var offset = (date - DateTime.Today).Days;
        return offset switch
        {
            0 => TodayLabel,
            1 => TomorrowLabel,
            _ => date.ToString("ddd d")
        };
    }

    private static string FormatDuration(double totalMinutes)
    {
        var minutes = WholeMinutes(totalMinutes);
        if (minutes >= 24 * 60)
        {
            var hoursLeft = minutes % (24 * 60) / 60;
            return hoursLeft == 0 ? $"{minutes / (24 * 60)} d" : $"{minutes / (24 * 60)} d {hoursLeft} h";
        }

        if (minutes >= 60)
        {
            return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
        }

        return $"{minutes} min";
    }

    private void SetChipSelected(Button button, bool selected)
        => button.Style = (Style)Resources[selected ? "DialChipSelectedStyle" : "DialChipStyle"];

    private void BuildTicks()
    {
        // 60 ticks, one every 12 minutes; every fifth is an hour, every fifteenth a quarter (12, 3, 6, 9).
        var minor = new GeometryGroup();
        var hours = new GeometryGroup();
        for (var tick = 0; tick < 60; tick++)
        {
            var isHour = tick % 5 == 0;
            var inner = tick % 15 == 0 ? 63 : isHour ? 67 : 71;
            var line = new LineGeometry { StartPoint = PointAt(tick * 6, inner), EndPoint = PointAt(tick * 6, 75) };
            (isHour ? hours : minor).Children.Add(line);
        }

        MinorTicks.Data = minor;
        HourTicks.Data = hours;
    }

    private void BuildPresets()
    {
        for (var i = 0; i < Presets.Length; i++)
        {
            var minutes = Presets[i];
            PresetsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = minutes < 60 ? $"+{minutes} min" : $"+{minutes / 60} h";
            var button = new Button { Content = label, Style = (Style)Resources["DialChipStyle"] };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => SetTargetFromInput(CeilingToMinute(DateTime.Now).AddMinutes(minutes));
            Grid.SetColumn(button, i);
            PresetsGrid.Children.Add(button);
            _presetButtons.Add((minutes, button));
        }
    }

    private void BuildDays()
    {
        // Today, tomorrow and the next two days as chips; the calendar button covers the rest of the week.
        for (var offset = 0; offset < 4; offset++)
        {
            var dayOffset = offset;
            DaysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var button = new Button { Style = (Style)Resources["DialChipStyle"] };
            button.Click += (_, _) => SetTargetFromInput(DateTime.Today.AddDays(dayOffset) + Target.TimeOfDay);
            Grid.SetColumn(button, offset);
            DaysGrid.Children.Add(button);
            _dayButtons.Add((offset, button));
        }

        DaysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var calendar = new CalendarView { SelectionMode = CalendarViewSelectionMode.Single };
        var flyout = new Flyout { Content = calendar, Placement = FlyoutPlacementMode.Bottom };
        flyout.Opening += (_, _) =>
        {
            calendar.MinDate = DateTime.Today;
            calendar.MaxDate = DateTime.Today.AddDays(MaxDaysAhead);
            calendar.SelectedDates.Clear();
            calendar.SelectedDates.Add(Target.Date);
            calendar.SetDisplayDate(Target.Date);
        };
        calendar.SelectedDatesChanged += (_, args) =>
        {
            // Clearing the selection on open raises this with nothing added; only react to a real pick.
            if (args.AddedDates.Count > 0 && args.AddedDates[0].Date != Target.Date)
            {
                SetTargetFromInput(args.AddedDates[0].Date + Target.TimeOfDay);
                flyout.Hide();
            }
        };

        _calendarButton = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 13 },
            Width = 40,
            Style = (Style)Resources["DialChipStyle"],
            Flyout = flyout
        };
        Grid.SetColumn(_calendarButton, 4);
        DaysGrid.Children.Add(_calendarButton);
    }

    private void OnDialPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        DialSurface.CapturePointer(e.Pointer);
        ApplyPointer(e);
        e.Handled = true;
    }

    private void OnDialPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (DialSurface.PointerCaptures?.Count > 0)
        {
            ApplyPointer(e);
            e.Handled = true;
        }
    }

    private void OnDialPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        DialSurface.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    // The pointer gives a position on the 12-hour face; move the target by the shortest way round from where it is,
    // so dragging continuously across 12 o'clock carries on into the next half-day instead of jumping back.
    private void ApplyPointer(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(DialSurface).Position;
        var degrees = Math.Atan2(position.X - Center, Center - position.Y) * 180 / Math.PI;
        if (degrees < 0) degrees += 360;
        var pointerMinute = (int)Math.Round(degrees * 2 / DragStepMinutes) * DragStepMinutes % 720;

        var current = Target;
        var currentMinute = current.Hour % 12 * 60 + current.Minute;
        var delta = pointerMinute - currentMinute;
        if (delta > 360) delta -= 720;
        else if (delta <= -360) delta += 720;

        SetTargetFromInput(current.AddMinutes(delta));
    }

    private void OnDialPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(DialSurface).Properties.MouseWheelDelta;
        SetTargetFromInput(Target.AddMinutes(delta > 0 ? DragStepMinutes : -DragStepMinutes));
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var step = e.Key switch
        {
            VirtualKey.Up or VirtualKey.Right => 1,
            VirtualKey.Down or VirtualKey.Left => -1,
            VirtualKey.PageUp => 15,
            VirtualKey.PageDown => -15,
            _ => 0
        };

        if (step != 0)
        {
            SetTargetFromInput(Target.AddMinutes(step));
            e.Handled = true;
        }
    }

    // 0° is 12 o'clock, clockwise; 720 minutes make a full turn.
    private static double ClockAngle(DateTime time) => (time.Hour % 12 * 60 + time.Minute + time.Second / 60.0) / 2;

    private static double MinutesUntil(DateTime target) => WholeMinutes((target - DateTime.Now).TotalMinutes);

    // Under a minute left still reads as "1 min" rather than "0 min".
    private static int WholeMinutes(double totalMinutes) => Math.Max(1, (int)Math.Floor(totalMinutes));

    private static DateTime CeilingToMinute(DateTime value)
    {
        var truncated = TruncateToMinute(value);
        return truncated == value ? truncated : truncated.AddMinutes(1);
    }

    private static DateTime TruncateToMinute(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

    private static Geometry BuildArc(double startAngle, double sweep)
    {
        var figure = new PathFigure { StartPoint = PointAt(startAngle, RingRadius), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointAt(startAngle + sweep, RingRadius),
            Size = new Size(RingRadius, RingRadius),
            IsLargeArc = sweep > 180,
            SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointAt(double degrees, double radius)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(Center + radius * Math.Sin(radians), Center - radius * Math.Cos(radians));
    }

    // Exposed as a slider over "minutes from now", so screen readers and automation can read and set it.
    private sealed partial class TargetTimeDialAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
    {
        private readonly TargetTimeDial _owner;

        public TargetTimeDialAutomationPeer(TargetTimeDial owner) : base(owner)
        {
            _owner = owner;
        }

        public bool IsReadOnly => false;
        public double LargeChange => 15;
        public double SmallChange => 1;
        public double Minimum => 1;
        public double Maximum => MinutesUntil(MaximumTarget);
        public double Value => MinutesUntil(_owner.Target);

        public void SetValue(double value) => _owner.SetTargetFromInput(CeilingToMinute(DateTime.Now).AddMinutes(value));

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => nameof(TargetTimeDial);

        protected override string GetItemStatusCore() => $"{_owner.TimeText.Text} {_owner.MeridiemText.Text} {_owner.DurationText.Text} {_owner.DayText.Text}".Trim();

        protected override object GetPatternCore(PatternInterface patternInterface)
            => patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);
    }
}
