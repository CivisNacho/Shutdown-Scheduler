using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Shutdown_Scheduler.Controls;

public partial class DateTimePicker : System.Windows.Controls.UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(DateTime?),
            typeof(DateTimePicker),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged,
                CoerceValue));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(DateTime?), typeof(DateTimePicker),
            new PropertyMetadata(null, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(DateTime?), typeof(DateTimePicker),
            new PropertyMetadata(null, OnRangeChanged));

    public static readonly DependencyProperty MinuteStepProperty =
        DependencyProperty.Register(nameof(MinuteStep), typeof(int), typeof(DateTimePicker),
            new PropertyMetadata(5, OnMinuteStepChanged, CoerceMinuteStep));

    public static readonly DependencyProperty DisplayFormatProperty =
        DependencyProperty.Register(nameof(DisplayFormat), typeof(string), typeof(DateTimePicker),
            new PropertyMetadata(string.Empty, OnDisplayFormatChanged));

    public static readonly DependencyProperty IsNullableProperty =
        DependencyProperty.Register(nameof(IsNullable), typeof(bool), typeof(DateTimePicker),
            new PropertyMetadata(false, OnIsNullableChanged));

    private bool _isSyncing;
    private bool _isPopupOpen;
    private bool _isDatePopupOpen;
    private DateTime? _selectedDate;
    private DateTime _displayDate = DateTime.Today;
    private string _calendarTitle = string.Empty;
    private bool _canNavigatePreviousMonth;
    private bool _canNavigateNextMonth;
    private int _pendingHour = 12;
    private int _pendingMinute;
    private string _pendingPeriod = "AM";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TimeColumnOption> HourColumn { get; } = new();
    public ObservableCollection<TimeColumnOption> MinuteColumn { get; } = new();
    public ObservableCollection<TimeColumnOption> PeriodColumn { get; } = new();
    public ObservableCollection<string> WeekdayLabels { get; } = new();
    public ObservableCollection<CalendarDayOption> CalendarDays { get; } = new();

    public DateTimePicker()
    {
        InitializeComponent();
        BuildWeekdayLabels();
        SyncFromValue();
        RebuildCalendar();
    }

    public DateTime? Value
    {
        get => (DateTime?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public DateTime? Minimum
    {
        get => (DateTime?)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public DateTime? Maximum
    {
        get => (DateTime?)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int MinuteStep
    {
        get => (int)GetValue(MinuteStepProperty);
        set => SetValue(MinuteStepProperty, value);
    }

    public string DisplayFormat
    {
        get => (string)GetValue(DisplayFormatProperty);
        set => SetValue(DisplayFormatProperty, value);
    }

    public bool IsNullable
    {
        get => (bool)GetValue(IsNullableProperty);
        set => SetValue(IsNullableProperty, value);
    }

    public bool IsPopupOpen
    {
        get => _isPopupOpen;
        set
        {
            if (_isPopupOpen == value) return;
            _isPopupOpen = value;
            if (value)
            {
                IsDatePopupOpen = false;
                SyncPendingFromValue();
            }

            OnPropertyChanged(nameof(IsPopupOpen));
        }
    }

    public bool IsDatePopupOpen
    {
        get => _isDatePopupOpen;
        set
        {
            if (_isDatePopupOpen == value) return;
            _isDatePopupOpen = value;
            if (value)
            {
                IsPopupOpen = false;
                EnsureCalendarDate();
            }

            OnPropertyChanged(nameof(IsDatePopupOpen));
        }
    }

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (_selectedDate == value) return;
            _selectedDate = value;
            OnPropertyChanged(nameof(SelectedDate));
        }
    }

    public DateTime DisplayDate
    {
        get => _displayDate;
        set
        {
            var month = new DateTime(value.Year, value.Month, 1);
            if (_displayDate == month) return;
            _displayDate = month;
            OnPropertyChanged(nameof(DisplayDate));
            RebuildCalendar();
        }
    }

    public string CalendarTitle
    {
        get => _calendarTitle;
        private set
        {
            if (_calendarTitle == value) return;
            _calendarTitle = value;
            OnPropertyChanged(nameof(CalendarTitle));
        }
    }

    public bool CanNavigatePreviousMonth
    {
        get => _canNavigatePreviousMonth;
        private set
        {
            if (_canNavigatePreviousMonth == value) return;
            _canNavigatePreviousMonth = value;
            OnPropertyChanged(nameof(CanNavigatePreviousMonth));
        }
    }

    public bool CanNavigateNextMonth
    {
        get => _canNavigateNextMonth;
        private set
        {
            if (_canNavigateNextMonth == value) return;
            _canNavigateNextMonth = value;
            OnPropertyChanged(nameof(CanNavigateNextMonth));
        }
    }

    public string DisplayText
    {
        get
        {
            if (Value is null) return string.Empty;
            var format = string.IsNullOrWhiteSpace(DisplayFormat)
                ? $"{CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern} HH:mm"
                : DisplayFormat;
            return Value.Value.ToString(format, CultureInfo.CurrentCulture);
        }
    }

    public Visibility ClearButtonVisibility => IsNullable ? Visibility.Visible : Visibility.Collapsed;

    private static object? CoerceValue(DependencyObject d, object? baseValue)
    {
        var picker = (DateTimePicker)d;
        if (baseValue is not DateTime value)
        {
            return picker.IsNullable ? null : picker.Clamp(DateTime.Now);
        }

        return picker.Clamp(value);
    }

    private static object CoerceMinuteStep(DependencyObject d, object baseValue)
    {
        var step = (int)baseValue;
        if (step < 1) return 1;
        if (step > 60) return 60;
        return step;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (DateTimePicker)d;
        picker.SyncFromValue();
        picker.OnPropertyChanged(nameof(DisplayText));
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (DateTimePicker)d;
        picker.CoerceValue(ValueProperty);
        picker.RebuildCalendar();
    }

    private static void OnMinuteStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (DateTimePicker)d;
        picker.CoerceValue(ValueProperty);
        picker.SyncFromValue();
    }

    private static void OnIsNullableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (DateTimePicker)d;
        picker.CoerceValue(ValueProperty);
        picker.OnPropertyChanged(nameof(ClearButtonVisibility));
    }

    private static void OnDisplayFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DateTimePicker)d).OnPropertyChanged(nameof(DisplayText));
    }

    private void SyncFromValue()
    {
        if (_isSyncing) return;

        _isSyncing = true;
        try
        {
            var value = Value ?? DateTime.Now;
            var selectedDate = Value?.Date;
            _selectedDate = selectedDate;
            var displaySource = selectedDate ?? DateTime.Today;
            _displayDate = new DateTime(displaySource.Year, displaySource.Month, 1);
            OnPropertyChanged(nameof(SelectedDate));
            OnPropertyChanged(nameof(DisplayDate));
            OnPropertyChanged(nameof(DisplayText));
            RebuildCalendar();
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void EnsureCalendarDate()
    {
        var value = Value ?? DateTime.Now;
        var date = value.Date;
        if (Minimum.HasValue && date < Minimum.Value.Date) date = Minimum.Value.Date;
        if (Maximum.HasValue && date > Maximum.Value.Date) date = Maximum.Value.Date;

        _isSyncing = true;
        try
        {
            _selectedDate = date;
            _displayDate = new DateTime(date.Year, date.Month, 1);
            OnPropertyChanged(nameof(SelectedDate));
            OnPropertyChanged(nameof(DisplayDate));
            RebuildCalendar();
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private DateTime Clamp(DateTime value)
    {
        var snapped = SnapDateTime(value, SnapMode.Nearest);
        if (Minimum.HasValue && snapped < Minimum.Value) return SnapDateTime(Minimum.Value, SnapMode.Up);
        if (Maximum.HasValue && snapped > Maximum.Value) return SnapDateTime(Maximum.Value, SnapMode.Down);
        return snapped;
    }

    private DateTime SnapDateTime(DateTime value, SnapMode mode)
    {
        var step = Math.Max(1, MinuteStep);
        var totalMinutes = (value.Hour * 60) + value.Minute;
        if (mode == SnapMode.Up && (value.Second > 0 || value.Millisecond > 0 || value.Microsecond > 0 || value.Nanosecond > 0))
        {
            totalMinutes++;
        }

        var snappedMinutes = mode switch
        {
            SnapMode.Up => (int)Math.Ceiling(totalMinutes / (double)step) * step,
            SnapMode.Down => (totalMinutes / step) * step,
            _ => (int)Math.Round(totalMinutes / (double)step, MidpointRounding.AwayFromZero) * step
        };

        return value.Date.AddMinutes(snappedMinutes);
    }

    private int SnapMinute(int minute)
    {
        var step = Math.Max(1, MinuteStep);
        var snapped = (int)Math.Round(minute / (double)step, MidpointRounding.AwayFromZero) * step;
        return Math.Clamp(snapped, 0, 60 - step);
    }

    private void SyncPendingFromValue()
    {
        var value = Value ?? DateTime.Now;
        var hour = value.Hour % 12;
        _pendingHour = hour == 0 ? 12 : hour;
        _pendingMinute = SnapMinute(value.Minute);
        _pendingPeriod = value.Hour >= 12 ? "PM" : "AM";
        RebuildTimeColumns();
    }

    private void RebuildTimeColumns()
    {
        HourColumn.Clear();
        for (var offset = -2; offset <= 2; offset++)
        {
            var hour = Wrap(_pendingHour + offset, 1, 12);
            HourColumn.Add(new TimeColumnOption(hour.ToString(CultureInfo.CurrentCulture), hour, hour == _pendingHour, true));
        }

        MinuteColumn.Clear();
        for (var offset = -2; offset <= 2; offset++)
        {
            var minute = Wrap(_pendingMinute + offset, 0, 59);
            MinuteColumn.Add(new TimeColumnOption(minute.ToString("00", CultureInfo.CurrentCulture), minute, minute == _pendingMinute, true));
        }

        PeriodColumn.Clear();
        if (_pendingPeriod == "AM")
        {
            PeriodColumn.Add(TimeColumnOption.Empty);
            PeriodColumn.Add(TimeColumnOption.Empty);
            PeriodColumn.Add(new TimeColumnOption("AM", 0, true, true));
            PeriodColumn.Add(new TimeColumnOption("PM", 1, false, true));
            PeriodColumn.Add(TimeColumnOption.Empty);
        }
        else
        {
            PeriodColumn.Add(TimeColumnOption.Empty);
            PeriodColumn.Add(new TimeColumnOption("AM", 0, false, true));
            PeriodColumn.Add(new TimeColumnOption("PM", 1, true, true));
            PeriodColumn.Add(TimeColumnOption.Empty);
            PeriodColumn.Add(TimeColumnOption.Empty);
        }
    }

    private void BuildWeekdayLabels()
    {
        WeekdayLabels.Clear();
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;

        for (var index = 0; index < 7; index++)
        {
            var day = (DayOfWeek)(((int)firstDay + index) % 7);
            var label = dayNames[(int)day];
            WeekdayLabels.Add(label.Length > 0 ? label[..1].ToUpper(CultureInfo.CurrentCulture) : string.Empty);
        }
    }

    private void RebuildCalendar()
    {
        CalendarDays.Clear();
        var monthStart = new DateTime(DisplayDate.Year, DisplayDate.Month, 1);
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)monthStart.DayOfWeek - (int)firstDay + 7) % 7;
        var gridStart = monthStart.AddDays(-offset);
        var selectedDate = Value?.Date;
        var today = DateTime.Today;

        for (var index = 0; index < 42; index++)
        {
            var date = gridStart.AddDays(index);
            CalendarDays.Add(new CalendarDayOption(
                date.Day.ToString(CultureInfo.CurrentCulture),
                date,
                date.Month == monthStart.Month,
                date == today,
                selectedDate.HasValue && date == selectedDate.Value,
                IsDateInRange(date)));
        }

        CalendarTitle = monthStart.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        CanNavigatePreviousMonth = MonthHasAnySelectableDate(monthStart.AddMonths(-1));
        CanNavigateNextMonth = MonthHasAnySelectableDate(monthStart.AddMonths(1));
    }

    private bool MonthHasAnySelectableDate(DateTime month)
    {
        var monthStart = new DateTime(month.Year, month.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var rangeStart = Minimum?.Date ?? DateTime.MinValue.Date;
        var rangeEnd = Maximum?.Date ?? DateTime.MaxValue.Date;
        return monthEnd >= rangeStart && monthStart <= rangeEnd;
    }

    private bool IsDateInRange(DateTime date)
    {
        var day = date.Date;
        if (Minimum.HasValue && day < Minimum.Value.Date) return false;
        if (Maximum.HasValue && day > Maximum.Value.Date) return false;
        return true;
    }

    private static int Wrap(int value, int minimum, int maximum)
    {
        var count = maximum - minimum + 1;
        while (value < minimum) value += count;
        while (value > maximum) value -= count;
        return value;
    }

    private void HourOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: int hour }) return;
        if (hour < 1) return;
        _pendingHour = hour;
        RebuildTimeColumns();
    }

    private void MinuteOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: int minute }) return;
        if (minute < 0) return;
        _pendingMinute = minute;
        RebuildTimeColumns();
    }

    private void PeriodOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: string period }) return;
        if (string.IsNullOrEmpty(period)) return;
        _pendingPeriod = period;
        RebuildTimeColumns();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var hour = _pendingHour % 12;
        if (_pendingPeriod == "PM") hour += 12;
        var date = Value?.Date ?? DateTime.Today;
        Value = date.AddHours(hour).AddMinutes(_pendingMinute);
        IsPopupOpen = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsPopupOpen = false;
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        if (!CanNavigatePreviousMonth) return;
        DisplayDate = DisplayDate.AddMonths(-1);
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        if (!CanNavigateNextMonth) return;
        DisplayDate = DisplayDate.AddMonths(1);
    }

    private void CalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: DateTime date }) return;
        if (!IsDateInRange(date)) return;

        var currentTime = (Value ?? DateTime.Now).TimeOfDay;
        Value = date.Date.Add(currentTime);
        IsDatePopupOpen = false;
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private enum SnapMode
    {
        Nearest,
        Up,
        Down
    }

    public sealed record TimeColumnOption(string Text, int Value, bool IsSelected, bool IsEnabled)
    {
        public static TimeColumnOption Empty { get; } = new(string.Empty, -1, false, false);
    }

    public sealed record CalendarDayOption(
        string Text,
        DateTime Date,
        bool IsInCurrentMonth,
        bool IsToday,
        bool IsSelected,
        bool IsEnabled);
}
