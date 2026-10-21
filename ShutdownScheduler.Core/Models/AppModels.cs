using System.ComponentModel;

namespace Shutdown_Scheduler.Models;

public enum PowerAction
{
    Shutdown,
    Restart,
    Suspend,
    Hibernate,
    LogOff,
    LockSession,
    CloseApp
}

public enum ScheduleMode
{
    ExactTime,
    RelativeMinutes,
    DateTime,
    Daily,
    Weekly
}

public enum ExecutionMode
{
    Normal,
    ForcedAdmin
}

public enum ScheduleStatus
{
    Scheduled,
    WaitingForConditions,
    WarningShown,
    Postponed,
    Executing,
    Cancelled,
    Expired
}

public enum NetworkUnit
{
    Kbs,
    Mbs
}

public enum ConditionType
{
    Network,
    Cpu,
    Battery,
    AppIdle
}

public enum WidgetDisplayMode
{
    Countdown,
    ExecutionTime
}

public enum WidgetSizePreset
{
    Small,
    Medium,
    Large
}

public enum WidgetAnchor
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed class OptionItem<T>
{
    public OptionItem(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }
    public string Label { get; }
}

public sealed class AppTarget
{
    public AppTarget(string processName, string label, string executablePath)
    {
        ProcessName = processName;
        Label = label;
        ExecutablePath = executablePath;
    }

    public string ProcessName { get; }
    public string Label { get; }
    public string ExecutablePath { get; }
}

public sealed class ScheduleRequest
{
    public PowerAction Action { get; init; }
    public ScheduleMode ScheduleMode { get; init; }
    public ExecutionMode ExecutionMode { get; init; }
    public DateTime TargetTime { get; init; }
    public DayOfWeek? WeeklyDay { get; init; }
    public bool NetworkConditionEnabled { get; init; }
    public double NetworkThresholdBytesPerSecond { get; init; }
    public bool CpuConditionEnabled { get; init; }
    public double CpuThresholdPercent { get; init; }
    public bool BatteryConditionEnabled { get; init; }
    public double BatteryThresholdPercent { get; init; }
    public bool AppIdleConditionEnabled { get; init; }
    public string AppIdleProcessName { get; init; } = string.Empty;
    public string AppIdleProcessLabel { get; init; } = string.Empty;
    public double AppIdleMinutes { get; init; }
    public string TargetProcessName { get; init; } = string.Empty;
    public string TargetProcessLabel { get; init; } = string.Empty;
    public string DisplaySummary { get; init; } = string.Empty;
    public string ConditionsSummary { get; init; } = string.Empty;
}

public sealed class ActiveSchedule : INotifyPropertyChanged
{
    private DateTime _targetTime;
    private ScheduleStatus _status;
    private string _statusDetails = string.Empty;
    private bool _warningShown;

    public ActiveSchedule(ScheduleRequest request)
    {
        Request = request;
        _targetTime = request.TargetTime;
        _status = ScheduleStatus.Scheduled;
    }

    public ScheduleRequest Request { get; }

    public DateTime TargetTime
    {
        get => _targetTime;
        set
        {
            if (_targetTime == value) return;
            _targetTime = value;
            OnPropertyChanged(nameof(TargetTime));
        }
    }

    public ScheduleStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged(nameof(Status));
        }
    }

    public string StatusDetails
    {
        get => _statusDetails;
        set
        {
            if (_statusDetails == value) return;
            _statusDetails = value;
            OnPropertyChanged(nameof(StatusDetails));
        }
    }

    public bool WarningShown
    {
        get => _warningShown;
        set
        {
            if (_warningShown == value) return;
            _warningShown = value;
            OnPropertyChanged(nameof(WarningShown));
        }
    }

    public bool IsRecurring => Request.ScheduleMode is ScheduleMode.Daily or ScheduleMode.Weekly;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ConditionEvaluationResult
{
    public bool IsSatisfied { get; init; }
    public string Message { get; init; } = string.Empty;
    public double CpuAverage { get; init; }
    public double NetworkAverageBytesPerSecond { get; init; }
    public double BatteryPercent { get; init; }
}

// Latest activity of the app watched by the app-idle condition. IdleSinceUtc is null while the app is busy.
public sealed record AppActivitySnapshot(
    bool IsRunning,
    bool IsReadable,
    double CpuPercent,
    double IoBytesPerSecond,
    double GpuPercent,
    DateTime? IdleSinceUtc)
{
    public static AppActivitySnapshot Unknown { get; } = new(true, true, 0, 0, 0, null);
}

public sealed class WidgetSettings
{
    public bool Enabled { get; set; } = true;
    public bool ShowOnAllScreens { get; set; }
    public bool PreventSleep { get; set; }
    public WidgetDisplayMode DisplayMode { get; set; } = WidgetDisplayMode.Countdown;
    public WidgetSizePreset SizePreset { get; set; } = WidgetSizePreset.Medium;
    public WidgetAnchor Anchor { get; set; } = WidgetAnchor.BottomRight;
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 72;
}

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsMaximized { get; set; }
}
