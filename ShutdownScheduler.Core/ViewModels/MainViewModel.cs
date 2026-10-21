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
    private readonly BatteryMonitorService _battery;

    private OptionItem<PowerAction> _selectedAction;
    private OptionItem<ScheduleMode> _selectedScheduleMode;
    private OptionItem<ExecutionMode> _selectedExecutionMode;
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
    private bool _batteryConditionEnabled;
    private string _batteryThreshold = "20";
    private string _selectedHour = "00";
    private string _selectedMinute = "00";
    private string _activeSummary = string.Empty;
    private string _statusSummary = string.Empty;
    private string _remaining = "--:--:--";
    private string _activeHeadline = string.Empty;
    private string _activeDetail = string.Empty;
    private string _activeGlyph = AppGlyphs.NoSchedule;
    private bool _hasActiveSchedule;
    private bool _updatingTimeParts;
    private DateTime? _selectedTargetDateTime;
    private string _selectedAppProcessName = string.Empty;
    private string _selectedAppLabel = string.Empty;
    private IReadOnlyList<AppTarget> _runningApps = Array.Empty<AppTarget>();

    public MainViewModel(
        SchedulingService scheduler,
        IWidgetService widget,
        LocalizationService loc,
        IUserNotificationService notifications,
        BatteryMonitorService battery)
    {
        _scheduler = scheduler;
        _widget = widget;
        _loc = loc;
        _notifications = notifications;
        _battery = battery;

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
        AddBatteryConditionCommand = new RelayCommand(
            _ => AddCondition(ConditionType.Battery),
            _ => IsBatteryConditionAvailable && !HasCondition(ConditionType.Battery));
        AddAppIdleConditionCommand = new RelayCommand(_ => AddCondition(ConditionType.AppIdle), _ => !HasCondition(ConditionType.AppIdle));
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
    public ICommand AddBatteryConditionCommand { get; }
    public ICommand AddAppIdleConditionCommand { get; }
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
    public string DateLabel => _loc.T("Date");
    public string PickDatePlaceholder => _loc.T("PickDate");
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
                OnPropertyChanged(nameof(IsCloseAppAction));
            }
        }
    }

    public OptionItem<ScheduleMode> SelectedScheduleMode { get => _selectedScheduleMode; set => SetProperty(ref _selectedScheduleMode, value); }
    public OptionItem<ExecutionMode> SelectedExecutionMode { get => _selectedExecutionMode; set => SetProperty(ref _selectedExecutionMode, value); }
    public OptionItem<WidgetDisplayMode> SelectedWidgetMode
    {
        get => _selectedWidgetMode;
        set
        {
            if (SetProperty(ref _selectedWidgetMode, value))
            {
                ApplyWidget();
            }
        }
    }
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
            if (double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes))
            {
                value = Math.Max(1, minutes).ToString(CultureInfo.CurrentCulture);
            }

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
    public bool BatteryConditionEnabled { get => _batteryConditionEnabled; set => SetProperty(ref _batteryConditionEnabled, value); }
    public string BatteryThreshold { get => _batteryThreshold; set => SetProperty(ref _batteryThreshold, value); }
    public bool CanAddNetworkCondition => !HasCondition(ConditionType.Network);
    public bool CanAddCpuCondition => !HasCondition(ConditionType.Cpu);
    public bool CanAddBatteryCondition => IsBatteryConditionAvailable && !HasCondition(ConditionType.Battery);
    public bool CanAddAppIdleCondition => !HasCondition(ConditionType.AppIdle);
    public bool IsBatteryConditionAvailable => _battery.IsBatteryAvailable;
    public bool HasConditions => Conditions.Count > 0;
    public bool HasNoConditions => !HasConditions;
    public bool WidgetEnabled { get => _widget.Settings.Enabled; set { _widget.Settings.Enabled = value; OnPropertyChanged(); ApplyWidget(); } }
    public bool WidgetShowOnAllScreens { get => _widget.Settings.ShowOnAllScreens; set { _widget.Settings.ShowOnAllScreens = value; OnPropertyChanged(); ApplyWidget(); } }
    public double WidgetLeft { get => _widget.Settings.Left; set { _widget.Settings.Left = value; OnPropertyChanged(); } }
    public double WidgetTop { get => _widget.Settings.Top; set { _widget.Settings.Top = value; OnPropertyChanged(); } }
    public double WidgetWidth { get => _widget.Settings.Width; set { _widget.Settings.Width = value; OnPropertyChanged(); } }
    public double WidgetHeight { get => _widget.Settings.Height; set { _widget.Settings.Height = value; OnPropertyChanged(); } }
    public bool IsForcedModeAvailable => ActionExecutionService.SupportsForcedMode(SelectedAction.Value);
    public bool IsCloseAppAction => SelectedAction.Value == PowerAction.CloseApp;

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
    public string ActiveHeadline { get => _activeHeadline; set => SetProperty(ref _activeHeadline, value); }
    public string ActiveDetail { get => _activeDetail; set => SetProperty(ref _activeDetail, value); }
    public string ActiveGlyph { get => _activeGlyph; set => SetProperty(ref _activeGlyph, value); }

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

        if (SelectedAction.Value == PowerAction.CloseApp)
        {
            if (string.IsNullOrWhiteSpace(_selectedAppProcessName))
            {
                return Fail(_loc.T("ValidationAppSelection"), out request, out error);
            }
        }

        if (target <= now) return Fail(_loc.T("ValidationFutureDateTime"), out request, out error);
        if (target > DateTime.Today.AddDays(7).AddHours(23).AddMinutes(59).AddSeconds(59))
        {
            return Fail(_loc.T("ValidationOffsetRange"), out request, out error);
        }

        SyncConditionFlags();

        var networkCondition = Conditions.FirstOrDefault(condition => condition.Type == ConditionType.Network);
        var cpuCondition = Conditions.FirstOrDefault(condition => condition.Type == ConditionType.Cpu);
        var batteryCondition = Conditions.FirstOrDefault(condition => condition.Type == ConditionType.Battery);

        if (NetworkConditionEnabled && networkCondition is not null)
        {
            NetworkThreshold = networkCondition.NetworkThreshold;
        }

        if (CpuConditionEnabled && cpuCondition is not null)
        {
            CpuThreshold = cpuCondition.CpuThreshold;
        }

        if (BatteryConditionEnabled && batteryCondition is not null)
        {
            BatteryThreshold = batteryCondition.BatteryThreshold;
        }

        if (NetworkConditionEnabled &&
            (networkCondition is null || !double.TryParse(networkCondition.NetworkThreshold, out var networkThreshold) || networkThreshold < 0))
        {
            return Fail(_loc.T("ValidationNetworkThreshold"), out request, out error);
        }

        if (CpuConditionEnabled &&
            (cpuCondition is null || !double.TryParse(cpuCondition.CpuThreshold, out var cpuThreshold) || cpuThreshold < 0 || cpuThreshold > 100))
        {
            return Fail(_loc.T("ValidationCpuThreshold"), out request, out error);
        }

        if (BatteryConditionEnabled)
        {
            if (!IsBatteryConditionAvailable)
            {
                return Fail(_loc.T("ValidationBatteryUnavailable"), out request, out error);
            }

            if (batteryCondition is null ||
                !double.TryParse(batteryCondition.BatteryThreshold, out var batteryThreshold) ||
                batteryThreshold < 0 ||
                batteryThreshold > 100)
            {
                return Fail(_loc.T("ValidationBatteryThreshold"), out request, out error);
            }
        }

        var appIdleCondition = Conditions.FirstOrDefault(condition => condition.Type == ConditionType.AppIdle);
        if (appIdleCondition is not null)
        {
            if (appIdleCondition.SelectedAppTarget is null)
            {
                return Fail(_loc.T("ValidationAppIdleApp"), out request, out error);
            }

            if (!double.TryParse(appIdleCondition.AppIdleMinutes, out var idleMinutes) || idleMinutes < 1 || idleMinutes > 1440)
            {
                return Fail(_loc.T("ValidationAppIdleMinutes"), out request, out error);
            }
        }

        var thresholdBytes = NetworkConditionEnabled
            ? double.Parse(networkCondition!.NetworkThreshold) * (networkCondition.SelectedNetworkUnit.Value == NetworkUnit.Mbs ? 1024 * 1024 : 1024)
            : 0;
        var batteryThresholdPercent = BatteryConditionEnabled
            ? double.Parse(batteryCondition!.BatteryThreshold)
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
            CpuThresholdPercent = CpuConditionEnabled ? double.Parse(cpuCondition!.CpuThreshold) : 0,
            BatteryConditionEnabled = BatteryConditionEnabled,
            BatteryThresholdPercent = batteryThresholdPercent,
            AppIdleConditionEnabled = appIdleCondition is not null,
            AppIdleProcessName = appIdleCondition?.SelectedAppTarget?.ProcessName ?? string.Empty,
            AppIdleProcessLabel = appIdleCondition?.SelectedAppTarget?.Label ?? string.Empty,
            AppIdleMinutes = appIdleCondition is not null ? double.Parse(appIdleCondition.AppIdleMinutes) : 0,
            TargetProcessName = _selectedAppProcessName,
            TargetProcessLabel = _selectedAppLabel,
            DisplaySummary = BuildDisplaySummary(target),
            ConditionsSummary = BuildConditionsSummary(thresholdBytes, batteryThresholdPercent)
        };

        return true;
    }

    private string BuildConditionsSummary(double thresholdBytes, double batteryThresholdPercent)
    {
        var parts = new List<string>();
        if (NetworkConditionEnabled) parts.Add($"{_loc.T("NetworkShort")} < {ConditionEvaluationService.FormatBytes(thresholdBytes)}");
        if (CpuConditionEnabled) parts.Add($"{_loc.T("CpuShort")} < {Conditions.First(condition => condition.Type == ConditionType.Cpu).CpuThreshold}%");
        if (BatteryConditionEnabled) parts.Add($"{_loc.T("BatteryShort")} < {batteryThresholdPercent:0.0}%");
        if (Conditions.FirstOrDefault(condition => condition.Type == ConditionType.AppIdle) is { SelectedAppTarget: { } idleApp } appIdle)
        {
            parts.Add($"{idleApp.Label}: {_loc.T("AppIdleShort")} ≥ {appIdle.AppIdleMinutes} min");
        }

        return parts.Count == 0 ? _loc.T("NoConditions") : string.Join($" {_loc.T("And")} ", parts);
    }

    private string BuildDisplaySummary(DateTime target)
    {
        if (SelectedAction.Value == PowerAction.CloseApp && !string.IsNullOrWhiteSpace(_selectedAppLabel))
        {
            return $"{_loc.ActionLabel(SelectedAction.Value)}: {_selectedAppLabel} {_loc.T("At")} {target:G}";
        }

        return $"{_loc.ActionLabel(SelectedAction.Value)} {_loc.T("At")} {target:G}";
    }

    private void AddCondition(ConditionType type)
    {
        if (type == ConditionType.Battery && !IsBatteryConditionAvailable)
        {
            return;
        }

        if (HasCondition(type)) return;

        var item = new ConditionEditorItem(type, GetConditionLabel(type), _loc, NetworkUnits);
        if (type == ConditionType.AppIdle)
        {
            item.SetAppTargets(_runningApps);
        }

        Conditions.Add(item);
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
        => type switch
        {
            ConditionType.Network => _loc.T("NetworkBelow"),
            ConditionType.Cpu => _loc.T("CpuBelow"),
            ConditionType.Battery => _loc.T("BatteryBelow"),
            ConditionType.AppIdle => _loc.T("AppIdleBelow"),
            _ => _loc.T("AddCondition")
        };

    private void SyncConditionFlags()
    {
        NetworkConditionEnabled = HasCondition(ConditionType.Network);
        CpuConditionEnabled = HasCondition(ConditionType.Cpu);
        BatteryConditionEnabled = HasCondition(ConditionType.Battery);
    }

    private void RefreshConditionState()
    {
        OnPropertyChanged(nameof(CanAddNetworkCondition));
        OnPropertyChanged(nameof(CanAddCpuCondition));
        OnPropertyChanged(nameof(CanAddBatteryCondition));
        OnPropertyChanged(nameof(CanAddAppIdleCondition));
        OnPropertyChanged(nameof(IsBatteryConditionAvailable));
        OnPropertyChanged(nameof(HasConditions));
        OnPropertyChanged(nameof(HasNoConditions));
        (AddNetworkConditionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddCpuConditionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddBatteryConditionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddAppIdleConditionCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
    }

    private string FormatDayOffset(int offset)
    {
        if (offset == 0) return _loc.T("Today");
        return string.Format(_loc.T(offset == 1 ? "DayOffset" : "DayOffsetPlural"), offset);
    }

    public void SetSelectedAppTarget(AppTarget? target)
    {
        _selectedAppProcessName = target?.ProcessName ?? string.Empty;
        _selectedAppLabel = target?.Label ?? string.Empty;
    }

    // The running-app list is discovered by the window; app-idle conditions pick their target from it.
    public void SetRunningApps(IReadOnlyList<AppTarget> apps)
    {
        _runningApps = apps;
        foreach (var condition in Conditions.Where(condition => condition.Type == ConditionType.AppIdle))
        {
            condition.SetAppTargets(apps);
        }
    }

    private void ApplyRelativeMinutesToTarget()
    {
        if (!double.TryParse(RelativeMinutes, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes))
        {
            return;
        }

        minutes = Math.Max(1, minutes);

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
            ActiveHeadline = _loc.T("NoScheduleTitle");
            ActiveDetail = _loc.T("NoScheduleHint");
            ActiveGlyph = AppGlyphs.NoSchedule;
            return;
        }

        var remaining = active.TargetTime - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        Remaining = RemainingTimeFormatter.FormatHoursMinutesSeconds(remaining);
        ActiveSummary = $"{active.Request.DisplaySummary}\n{_loc.T("Mode")}: {_loc.ExecutionModeLabel(active.Request.ExecutionMode)}\n{_loc.T("Conditions")}: {active.Request.ConditionsSummary}";
        StatusSummary = $"{_loc.T("Status")}: {_loc.StatusLabel(active.Status)}\n{_loc.T("Remaining")}: {Remaining}\n{active.StatusDetails}";
        ActiveHeadline = active.Request.DisplaySummary;
        // While waiting, the reason (e.g. which condition is still pending) is more useful than the status name.
        ActiveDetail = string.IsNullOrWhiteSpace(active.StatusDetails)
            ? $"{_loc.StatusLabel(active.Status)} · {_loc.T("Conditions")}: {active.Request.ConditionsSummary}"
            : active.StatusDetails;
        ActiveGlyph = AppGlyphs.ForAction(active.Request.Action);
    }
}

public sealed class ConditionEditorItem : ObservableObject
{
    private string _networkThreshold = "500";
    private string _cpuThreshold = "20";
    private string _batteryThreshold = "20";
    private string _appIdleMinutes = "5";
    private AppTarget? _selectedAppTarget;
    private OptionItem<NetworkUnit> _selectedNetworkUnit;

    public ConditionEditorItem(
        ConditionType type,
        string label,
        LocalizationService loc,
        IEnumerable<OptionItem<NetworkUnit>> networkUnits)
    {
        Type = type;
        Label = label;
        RequiredBelowLabel = loc.T("RequiredBelow");
        NetworkShortLabel = loc.T("NetworkShort");
        CpuShortLabel = loc.T("CpuShort");
        BatteryShortLabel = loc.T("BatteryShort");
        AppIdleAppLabel = loc.T("AppIdleApp");
        AppIdleMinutesLabel = loc.T("AppIdleMinutes");
        RefreshAppsLabel = loc.T("RefreshApps");
        RemoveConditionLabel = loc.T("RemoveCondition");
        NetworkUnits = new ObservableCollection<OptionItem<NetworkUnit>>(networkUnits);
        _selectedNetworkUnit = NetworkUnits[0];
    }

    public ConditionType Type { get; }
    public string Label { get; }
    public string RequiredBelowLabel { get; }
    public string NetworkShortLabel { get; }
    public string CpuShortLabel { get; }
    public string BatteryShortLabel { get; }
    public string AppIdleAppLabel { get; }
    public string AppIdleMinutesLabel { get; }
    public string RefreshAppsLabel { get; }
    public string RemoveConditionLabel { get; }
    public ObservableCollection<OptionItem<NetworkUnit>> NetworkUnits { get; }
    public OptionItem<NetworkUnit> SelectedNetworkUnit { get => _selectedNetworkUnit; set => SetProperty(ref _selectedNetworkUnit, value); }
    public string NetworkThreshold { get => _networkThreshold; set => SetProperty(ref _networkThreshold, value); }
    public string CpuThreshold { get => _cpuThreshold; set => SetProperty(ref _cpuThreshold, value); }
    public string BatteryThreshold { get => _batteryThreshold; set => SetProperty(ref _batteryThreshold, value); }
    public string AppIdleMinutes { get => _appIdleMinutes; set => SetProperty(ref _appIdleMinutes, value); }
    public ObservableCollection<AppTarget> AppTargets { get; } = new();
    public AppTarget? SelectedAppTarget { get => _selectedAppTarget; set => SetProperty(ref _selectedAppTarget, value); }
    public bool IsNetwork => Type == ConditionType.Network;
    public bool IsCpu => Type == ConditionType.Cpu;
    public bool IsBattery => Type == ConditionType.Battery;
    public bool IsAppIdle => Type == ConditionType.AppIdle;
    public string Glyph => AppGlyphs.ForCondition(Type);

    // Keeps the chosen app selected across refreshes as long as it is still running.
    public void SetAppTargets(IEnumerable<AppTarget> apps)
    {
        var selectedProcessName = SelectedAppTarget?.ProcessName;
        AppTargets.Clear();
        foreach (var app in apps)
        {
            AppTargets.Add(app);
        }

        SelectedAppTarget = AppTargets.FirstOrDefault(app =>
            string.Equals(app.ProcessName, selectedProcessName, StringComparison.OrdinalIgnoreCase));
    }
}
