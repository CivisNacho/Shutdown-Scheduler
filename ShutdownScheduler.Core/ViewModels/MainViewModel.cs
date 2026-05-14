using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Services;

namespace Shutdown_Scheduler.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SchedulingService _scheduler;
    private readonly IWidgetService _widget;
    private readonly LocalizationService _loc;
    private readonly IUserNotificationService _notifications;

    private OptionItem<PowerAction> _selectedAction;
    private OptionItem<ScheduleMode> _selectedScheduleMode;
    private OptionItem<ExecutionMode> _selectedExecutionMode;
    private OptionItem<NetworkUnit> _selectedNetworkUnit;
    private OptionItem<WidgetDisplayMode> _selectedWidgetMode;
    private OptionItem<WidgetSizePreset> _selectedWidgetSizePreset;
    private OptionItem<WidgetAnchor> _selectedWidgetAnchor;
    private OptionItem<int> _selectedDayOffset;
    private OptionItem<DayOfWeek> _selectedWeeklyDay;
    private string _exactTime = DateTime.Now.AddHours(1).ToString("HH:mm");
    private string _relativeMinutes = "45";
    private string _targetTime = DateTime.Now.AddHours(1).ToString("HH:mm");
    private bool _isForcedAdmin;
    private DateTime _selectedDate = DateTime.Today;
    private string _dateTimeValue = DateTime.Now.AddHours(1).ToString("HH:mm");
    private string _dailyTime = "01:00";
    private string _weeklyTime = "23:45";
    private bool _networkConditionEnabled;
    private string _networkThreshold = "500";
    private bool _cpuConditionEnabled;
    private string _cpuThreshold = "20";
    private string _selectedHour = "00";
    private string _selectedMinute = "00";
    private string _activeSummary = string.Empty;
    private string _statusSummary = string.Empty;
    private string _remaining = "--:--:--";
    private bool _hasActiveSchedule;
    private bool _updatingTimeParts;
    private DateTime? _selectedTargetDateTime;

    public MainViewModel(SchedulingService scheduler, IWidgetService widget, LocalizationService loc, IUserNotificationService notifications)
    {
        _scheduler = scheduler;
        _widget = widget;
        _loc = loc;
        _notifications = notifications;

        Actions = new(Enum.GetValues<PowerAction>().Select(action => new OptionItem<PowerAction>(action, loc.ActionLabel(action))));
        ScheduleModes = new(Enum.GetValues<ScheduleMode>().Select(mode => new OptionItem<ScheduleMode>(mode, loc.ScheduleModeLabel(mode))));
        ExecutionModes = new(Enum.GetValues<ExecutionMode>().Select(mode => new OptionItem<ExecutionMode>(mode, loc.ExecutionModeLabel(mode))));
        NetworkUnits = new(new[] { new OptionItem<NetworkUnit>(NetworkUnit.Kbs, "KB/s"), new OptionItem<NetworkUnit>(NetworkUnit.Mbs, "MB/s") });
        WidgetModes = new(new[] { new OptionItem<WidgetDisplayMode>(WidgetDisplayMode.Countdown, loc.T("WidgetCountdown")), new OptionItem<WidgetDisplayMode>(WidgetDisplayMode.ExecutionTime, loc.T("WidgetExecution")) });
        WidgetSizePresets = new(new[]
        {
            new OptionItem<WidgetSizePreset>(WidgetSizePreset.Small, loc.T("WidgetSmall")),
            new OptionItem<WidgetSizePreset>(WidgetSizePreset.Medium, loc.T("WidgetMedium")),
            new OptionItem<WidgetSizePreset>(WidgetSizePreset.Large, loc.T("WidgetLarge"))
        });
        WidgetAnchors = new(new[]
        {
            new OptionItem<WidgetAnchor>(WidgetAnchor.BottomRight, loc.T("WidgetAnchorBottomRight")),
            new OptionItem<WidgetAnchor>(WidgetAnchor.BottomLeft, loc.T("WidgetAnchorBottomLeft")),
            new OptionItem<WidgetAnchor>(WidgetAnchor.TopRight, loc.T("WidgetAnchorTopRight")),
            new OptionItem<WidgetAnchor>(WidgetAnchor.TopLeft, loc.T("WidgetAnchorTopLeft"))
        });
        DayOffsets = new(Enumerable.Range(0, 8).Select(offset => new OptionItem<int>(offset, FormatDayOffset(offset))));
        Weekdays = new(Enum.GetValues<DayOfWeek>().Select(day => new OptionItem<DayOfWeek>(day, loc.WeekdayLabel(day))));
        HourOptions = new(Enumerable.Range(0, 24).Select(h => h.ToString("00")));
        MinuteOptions = new(Enumerable.Range(0, 60).Where(m => m % 5 == 0).Select(m => m.ToString("00")));

        _selectedAction = Actions[0];
        _selectedScheduleMode = ScheduleModes[0];
        _selectedExecutionMode = ExecutionModes[0];
        _selectedNetworkUnit = NetworkUnits[0];
        _selectedWidgetMode = WidgetModes.First(item => item.Value == widget.Settings.DisplayMode);
        _selectedWidgetSizePreset = WidgetSizePresets.FirstOrDefault(item => item.Value == widget.Settings.SizePreset) ?? WidgetSizePresets[1];
        _selectedWidgetAnchor = WidgetAnchors.FirstOrDefault(item => item.Value == widget.Settings.Anchor) ?? WidgetAnchors[0];
        var initialTarget = DateTime.Now.AddMinutes(45);
        _targetTime = initialTarget.ToString("HH:mm");
        _selectedTargetDateTime = initialTarget;
        SyncTimePartsFromTarget();
        _selectedDayOffset = DayOffsets.First(item => item.Value == Math.Clamp((initialTarget.Date - DateTime.Today).Days, 0, 7));
        _selectedWeeklyDay = Weekdays.First(item => item.Value == DayOfWeek.Friday);
        _isForcedAdmin = _selectedExecutionMode.Value == ExecutionMode.ForcedAdmin;

        ScheduleCommand = new RelayCommand(_ => Schedule());
        CancelCommand = new RelayCommand(_ => _scheduler.Cancel(), _ => HasActiveSchedule);
        ApplyWidgetCommand = new RelayCommand(_ => ApplyWidget());
        AddNetworkConditionCommand = new RelayCommand(_ => AddCondition(ConditionType.Network), _ => !HasCondition(ConditionType.Network));
        AddCpuConditionCommand = new RelayCommand(_ => AddCondition(ConditionType.Cpu), _ => !HasCondition(ConditionType.Cpu));
        RemoveConditionCommand = new RelayCommand(condition =>
        {
            if (condition is ConditionEditorItem item)
            {
                RemoveCondition(item);
            }
        });

        _scheduler.ScheduleChanged += (_, _) => RefreshActiveState();
        _scheduler.Tick += (_, _) => RefreshActiveState();
        RefreshActiveState();
    }

    public LocalizationService Loc => _loc;
    public ObservableCollection<OptionItem<PowerAction>> Actions { get; }
    public ObservableCollection<OptionItem<ScheduleMode>> ScheduleModes { get; }
    public ObservableCollection<OptionItem<ExecutionMode>> ExecutionModes { get; }
    public ObservableCollection<OptionItem<NetworkUnit>> NetworkUnits { get; }
    public ObservableCollection<ConditionEditorItem> Conditions { get; } = new();
    public ObservableCollection<OptionItem<WidgetDisplayMode>> WidgetModes { get; }
    public ObservableCollection<OptionItem<WidgetSizePreset>> WidgetSizePresets { get; }
    public ObservableCollection<OptionItem<WidgetAnchor>> WidgetAnchors { get; }
    public ObservableCollection<OptionItem<int>> DayOffsets { get; }
    public ObservableCollection<OptionItem<DayOfWeek>> Weekdays { get; }
    public ObservableCollection<string> HourOptions { get; }
    public ObservableCollection<string> MinuteOptions { get; }
    public ICommand ScheduleCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ApplyWidgetCommand { get; }
    public ICommand AddNetworkConditionCommand { get; }
    public ICommand AddCpuConditionCommand { get; }
    public ICommand RemoveConditionCommand { get; }

    public string AppTitle => _loc.T("AppTitle");
    public string Subtitle => _loc.T("Subtitle");
    public string ActionLabel => _loc.T("Action");
    public string ScheduleLabel => _loc.T("Schedule");
    public string ExecutionLabel => _loc.T("Force admin?");
    public string ConditionsLabel => _loc.T("Conditions");
    public string AddConditionLabel => _loc.T("AddCondition");
    public string AddConditionDialogTitle => _loc.T("AddConditionDialogTitle");
    public string RemoveConditionLabel => _loc.T("RemoveCondition");
    public string NoConditionsLabel => _loc.T("NoConditions");
    public string WidgetLabel => _loc.T("Widget");
    public string ActiveScheduleLabel => _loc.T("ActiveSchedule");
    public string ScheduleButtonLabel => HasActiveSchedule ? _loc.T("ReplaceButton") : _loc.T("ScheduleButton");
    public string CancelButtonLabel => _loc.T("CancelButton");
    public string ForceAdminLabel => _loc.T("ForceAdmin");
    public string YesLabel => _loc.T("Yes");
    public string NoLabel => _loc.T("No");
    public string DayLabel => _loc.T("Day");
    public string TargetTimeLabel => _loc.T("TargetTime");
    public string MinutesFromNowLabel => _loc.T("MinutesFromNow");
    public string ShowOnAllScreensLabel => _loc.T("ShowOnAllScreens");
    public string WidgetSizeLabel => _loc.T("WidgetSize");
    public string WidgetPositionLabel => _loc.T("WidgetPosition");
    public string ProgramLabel => _loc.T("Program");
    public string ProgramPanelText => _loc.T("ProgramPanelText");
    public DateTime MinimumTargetDateTime => DateTime.Now;
    public DateTime MaximumTargetDateTime => DateTime.Today.AddDays(7).AddHours(23).AddMinutes(59).AddSeconds(59);

    public OptionItem<PowerAction> SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (SetProperty(ref _selectedAction, value))
            {
                if (!ActionExecutionService.SupportsForcedMode(value.Value))
                {
                    IsForcedAdmin = false;
                }
                OnPropertyChanged(nameof(IsForcedModeAvailable));
            }
        }
    }

    public OptionItem<ScheduleMode> SelectedScheduleMode { get => _selectedScheduleMode; set => SetProperty(ref _selectedScheduleMode, value); }
    public OptionItem<ExecutionMode> SelectedExecutionMode { get => _selectedExecutionMode; set => SetProperty(ref _selectedExecutionMode, value); }
    public OptionItem<NetworkUnit> SelectedNetworkUnit { get => _selectedNetworkUnit; set => SetProperty(ref _selectedNetworkUnit, value); }
    public OptionItem<WidgetDisplayMode> SelectedWidgetMode { get => _selectedWidgetMode; set => SetProperty(ref _selectedWidgetMode, value); }
    public OptionItem<WidgetSizePreset> SelectedWidgetSizePreset
    {
        get => _selectedWidgetSizePreset;
        set
        {
            if (SetProperty(ref _selectedWidgetSizePreset, value))
            {
                _widget.Settings.SizePreset = value.Value;
                ApplyWidget();
            }
        }
    }
    public OptionItem<WidgetAnchor> SelectedWidgetAnchor
    {
        get => _selectedWidgetAnchor;
        set
        {
            if (SetProperty(ref _selectedWidgetAnchor, value))
            {
                _widget.Settings.Anchor = value.Value;
                ApplyWidget();
            }
        }
    }

    public OptionItem<int> SelectedDayOffset { get => _selectedDayOffset; set => SetProperty(ref _selectedDayOffset, value); }
    public OptionItem<DayOfWeek> SelectedWeeklyDay { get => _selectedWeeklyDay; set => SetProperty(ref _selectedWeeklyDay, value); }

    public string ExactTime { get => _exactTime; set => SetProperty(ref _exactTime, value); }
    public string RelativeMinutes
    {
        get => _relativeMinutes;
        set
        {
            if (SetProperty(ref _relativeMinutes, value))
            {
                ApplyRelativeMinutesToTarget();
            }
        }
    }

    public string TargetTime
    {
        get => _targetTime;
        set
        {
            if (SetProperty(ref _targetTime, value))
            {
                SyncTimePartsFromTarget();
            }
        }
    }
    public DateTime? SelectedTargetDateTime
    {
        get => _selectedTargetDateTime;
        set
        {
            if (SetProperty(ref _selectedTargetDateTime, value) && value.HasValue)
            {
                _targetTime = value.Value.ToString("HH:mm");
                OnPropertyChanged(nameof(TargetTime));
                SyncTimePartsFromTarget();
            }
        }
    }
    public string SelectedHour
    {
        get => _selectedHour;
        set
        {
            if (SetProperty(ref _selectedHour, value))
            {
                SyncTargetFromTimeParts();
            }
        }
    }
    public string SelectedMinute
    {
        get => _selectedMinute;
        set
        {
            if (SetProperty(ref _selectedMinute, value))
            {
                SyncTargetFromTimeParts();
            }
        }
    }
    public bool IsForcedAdmin
    {
        get => _isForcedAdmin;
        set
        {
            if (SetProperty(ref _isForcedAdmin, value))
            {
                SelectedExecutionMode = value ? ExecutionModes.First(item => item.Value == ExecutionMode.ForcedAdmin) : ExecutionModes[0];
                OnPropertyChanged(nameof(IsNormalExecution));
            }
        }
    }

    public bool IsNormalExecution
    {
        get => !IsForcedAdmin;
        set
        {
            if (value) IsForcedAdmin = false;
            OnPropertyChanged();
        }
    }

    public DateTime SelectedDate { get => _selectedDate; set => SetProperty(ref _selectedDate, value); }
    public string DateTimeValue { get => _dateTimeValue; set => SetProperty(ref _dateTimeValue, value); }
    public string DailyTime { get => _dailyTime; set => SetProperty(ref _dailyTime, value); }
    public string WeeklyTime { get => _weeklyTime; set => SetProperty(ref _weeklyTime, value); }
    public bool NetworkConditionEnabled { get => _networkConditionEnabled; set => SetProperty(ref _networkConditionEnabled, value); }
    public string NetworkThreshold { get => _networkThreshold; set => SetProperty(ref _networkThreshold, value); }
    public bool CpuConditionEnabled { get => _cpuConditionEnabled; set => SetProperty(ref _cpuConditionEnabled, value); }
    public string CpuThreshold { get => _cpuThreshold; set => SetProperty(ref _cpuThreshold, value); }
    public bool CanAddNetworkCondition => !HasCondition(ConditionType.Network);
    public bool CanAddCpuCondition => !HasCondition(ConditionType.Cpu);
    public bool HasConditions => Conditions.Count > 0;
    public bool HasNoConditions => !HasConditions;
    public bool WidgetEnabled { get => _widget.Settings.Enabled; set { _widget.Settings.Enabled = value; OnPropertyChanged(); ApplyWidget(); } }
    public bool WidgetShowOnAllScreens { get => _widget.Settings.ShowOnAllScreens; set { _widget.Settings.ShowOnAllScreens = value; OnPropertyChanged(); ApplyWidget(); } }
    public double WidgetLeft { get => _widget.Settings.Left; set { _widget.Settings.Left = value; OnPropertyChanged(); } }
    public double WidgetTop { get => _widget.Settings.Top; set { _widget.Settings.Top = value; OnPropertyChanged(); } }
    public double WidgetWidth { get => _widget.Settings.Width; set { _widget.Settings.Width = value; OnPropertyChanged(); } }
    public double WidgetHeight { get => _widget.Settings.Height; set { _widget.Settings.Height = value; OnPropertyChanged(); } }
    public bool IsForcedModeAvailable => ActionExecutionService.SupportsForcedMode(SelectedAction.Value);

    public bool HasActiveSchedule
    {
        get => _hasActiveSchedule;
        set
        {
            if (SetProperty(ref _hasActiveSchedule, value))
            {
                OnPropertyChanged(nameof(ScheduleButtonLabel));
                (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string ActiveSummary { get => _activeSummary; set => SetProperty(ref _activeSummary, value); }
    public string StatusSummary { get => _statusSummary; set => SetProperty(ref _statusSummary, value); }
    public string Remaining { get => _remaining; set => SetProperty(ref _remaining, value); }

    private void Schedule()
    {
        if (!TryBuildRequest(out var request, out var error))
        {
            _notifications.ShowWarning(_loc.T("ValidationTitle"), error);
            return;
        }

        _scheduler.Schedule(request);
    }

    private bool TryBuildRequest(out ScheduleRequest request, out string error)
    {
        request = new ScheduleRequest();
        error = string.Empty;
        var now = DateTime.Now;
        if (SelectedTargetDateTime is not { } target)
        {
            return Fail(_loc.T("ValidationExactTime"), out request, out error);
        }

        if (target <= now) return Fail(_loc.T("ValidationFutureDateTime"), out request, out error);
        if (target > DateTime.Today.AddDays(7).AddHours(23).AddMinutes(59).AddSeconds(59))
        {
            return Fail(_loc.T("ValidationOffsetRange"), out request, out error);
        }

        SyncConditionFlags();

        if (NetworkConditionEnabled && (!double.TryParse(NetworkThreshold, out var networkThreshold) || networkThreshold < 0))
        {
            return Fail(_loc.T("ValidationNetworkThreshold"), out request, out error);
        }

        if (CpuConditionEnabled && (!double.TryParse(CpuThreshold, out var cpuThreshold) || cpuThreshold < 0 || cpuThreshold > 100))
        {
            return Fail(_loc.T("ValidationCpuThreshold"), out request, out error);
        }

        var thresholdBytes = NetworkConditionEnabled
            ? double.Parse(NetworkThreshold) * (SelectedNetworkUnit.Value == NetworkUnit.Mbs ? 1024 * 1024 : 1024)
            : 0;

        request = new ScheduleRequest
        {
            Action = SelectedAction.Value,
            ScheduleMode = ScheduleMode.DateTime,
            ExecutionMode = IsForcedModeAvailable && IsForcedAdmin ? ExecutionMode.ForcedAdmin : ExecutionMode.Normal,
            TargetTime = target,
            WeeklyDay = null,
            NetworkConditionEnabled = NetworkConditionEnabled,
            NetworkThresholdBytesPerSecond = thresholdBytes,
            CpuConditionEnabled = CpuConditionEnabled,
            CpuThresholdPercent = CpuConditionEnabled ? double.Parse(CpuThreshold) : 0,
            DisplaySummary = $"{_loc.ActionLabel(SelectedAction.Value)} at {target:G}",
            ConditionsSummary = BuildConditionsSummary(thresholdBytes)
        };

        return true;
    }

    private string BuildConditionsSummary(double thresholdBytes)
    {
        var parts = new List<string>();
        if (NetworkConditionEnabled) parts.Add($"{_loc.T("NetworkShort")} < {ConditionEvaluationService.FormatBytes(thresholdBytes)}");
        if (CpuConditionEnabled) parts.Add($"{_loc.T("CpuShort")} < {CpuThreshold}%");
        return parts.Count == 0 ? _loc.T("NoConditions") : string.Join(" AND ", parts);
    }

    private void AddCondition(ConditionType type)
    {
        if (HasCondition(type)) return;

        Conditions.Add(new ConditionEditorItem(type, GetConditionLabel(type)));
        SyncConditionFlags();
        RefreshConditionState();
    }

    private void RemoveCondition(ConditionEditorItem item)
    {
        Conditions.Remove(item);
        SyncConditionFlags();
        RefreshConditionState();
    }

    private bool HasCondition(ConditionType type) => Conditions.Any(condition => condition.Type == type);

    private string GetConditionLabel(ConditionType type)
        => type == ConditionType.Network ? _loc.T("NetworkBelow") : _loc.T("CpuBelow");

    private void SyncConditionFlags()
    {
        NetworkConditionEnabled = HasCondition(ConditionType.Network);
        CpuConditionEnabled = HasCondition(ConditionType.Cpu);
    }

    private void RefreshConditionState()
    {
        OnPropertyChanged(nameof(CanAddNetworkCondition));
        OnPropertyChanged(nameof(CanAddCpuCondition));
        OnPropertyChanged(nameof(HasConditions));
        OnPropertyChanged(nameof(HasNoConditions));
        (AddNetworkConditionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddCpuConditionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static bool Fail(string message, out ScheduleRequest request, out string error)
    {
        request = new ScheduleRequest();
        error = message;
        return false;
    }

    private void ApplyWidget()
    {
        _widget.Settings.DisplayMode = SelectedWidgetMode.Value;
        _widget.Settings.SizePreset = SelectedWidgetSizePreset.Value;
        _widget.Settings.Anchor = SelectedWidgetAnchor.Value;
        _widget.ApplySettings();
        _widget.Update(_scheduler.ActiveSchedule);
    }

    private string FormatDayOffset(int offset)
    {
        if (offset == 0) return _loc.T("Today");
        return string.Format(_loc.T(offset == 1 ? "DayOffset" : "DayOffsetPlural"), offset);
    }

    private void ApplyRelativeMinutesToTarget()
    {
        if (!double.TryParse(RelativeMinutes, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes) || minutes <= 0)
        {
            return;
        }

        var target = DateTime.Now.AddMinutes(minutes);
        if (target > MaximumTargetDateTime) return;

        SelectedTargetDateTime = target;
    }

    private void SyncTargetFromTimeParts()
    {
        if (_updatingTimeParts) return;
        TargetTime = $"{SelectedHour}:{SelectedMinute}";
    }

    private void SyncTimePartsFromTarget()
    {
        if (!TimeSpan.TryParseExact(TargetTime, "hh\\:mm", CultureInfo.InvariantCulture, out var parsed))
        {
            return;
        }

        _updatingTimeParts = true;
        try
        {
            SelectedHour = parsed.Hours.ToString("00");
            SelectedMinute = parsed.Minutes.ToString("00");
        }
        finally
        {
            _updatingTimeParts = false;
        }
    }

    private void RefreshActiveState()
    {
        var active = _scheduler.ActiveSchedule;
        HasActiveSchedule = active is not null;
        if (active is null)
        {
            ActiveSummary = _loc.T("NoActiveSchedule");
            StatusSummary = string.Empty;
            Remaining = "--:--:--";
            _widget.Update(null);
            return;
        }

        var remaining = active.TargetTime - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        Remaining = RemainingTimeFormatter.FormatHoursMinutesSeconds(remaining);
        ActiveSummary = $"{active.Request.DisplaySummary}\n{_loc.T("Mode")}: {_loc.ExecutionModeLabel(active.Request.ExecutionMode)}\n{_loc.T("Conditions")}: {active.Request.ConditionsSummary}";
        StatusSummary = $"{_loc.T("Status")}: {_loc.StatusLabel(active.Status)}\n{_loc.T("Remaining")}: {Remaining}\n{active.StatusDetails}";
        _widget.Update(active);
    }
}

public sealed class ConditionEditorItem
{
    public ConditionEditorItem(ConditionType type, string label)
    {
        Type = type;
        Label = label;
    }

    public ConditionType Type { get; }
    public string Label { get; }
    public bool IsNetwork => Type == ConditionType.Network;
    public bool IsCpu => Type == ConditionType.Cpu;
}
