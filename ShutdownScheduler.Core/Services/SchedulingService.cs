using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler.Services;

public sealed class SchedulingService
{
    private readonly IAppTimer _timer;
    private readonly ConditionEvaluationService _conditions;
    private readonly ActionExecutionService _executor;
    private readonly LocalizationService _loc;
    private DateTime _lastTick = DateTime.Now;

    public SchedulingService(ConditionEvaluationService conditions, ActionExecutionService executor, LocalizationService loc, IAppTimerFactory timerFactory)
    {
        _conditions = conditions;
        _executor = executor;
        _loc = loc;
        _timer = timerFactory.Create(TimeSpan.FromSeconds(1));
        _timer.Tick += OnTick;
    }

    public ActiveSchedule? ActiveSchedule { get; private set; }

    public event EventHandler? ScheduleChanged;
    public event EventHandler? Tick;
    public event EventHandler<ActiveSchedule>? WarningDue;
    public event EventHandler<ScheduleAlert>? AlertDue;
    public event EventHandler<string>? Error;

    public void Schedule(ScheduleRequest request)
    {
        ActiveSchedule = new ActiveSchedule(request);
        MarkAlertsAlreadyPassed(ActiveSchedule, DateTime.Now);
        _conditions.BeginSchedule(request);
        _lastTick = DateTime.Now;
        _timer.Start();
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        if (ActiveSchedule is not null)
        {
            ActiveSchedule.Status = ScheduleStatus.Cancelled;
        }

        ActiveSchedule = null;
        _conditions.EndSchedule();
        _timer.Stop();
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Postpone()
    {
        if (ActiveSchedule is null) return;
        ActiveSchedule.TargetTime = ActiveSchedule.TargetTime.AddMinutes(5);
        ActiveSchedule.WarningShown = false;
        ActiveSchedule.ExecutionPendingUntil = null;
        MarkAlertsAlreadyPassed(ActiveSchedule, DateTime.Now);
        ActiveSchedule.Status = ScheduleStatus.Postponed;
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (ActiveSchedule is null)
        {
            _timer.Stop();
            return;
        }

        var now = DateTime.Now;
        var uncontrolledGap = now - _lastTick;
        _lastTick = now;
        Tick?.Invoke(this, EventArgs.Empty);

        if (now >= ActiveSchedule.TargetTime.AddMinutes(10) && uncontrolledGap > TimeSpan.FromMinutes(2))
        {
            ActiveSchedule.Status = ScheduleStatus.Expired;
            ActiveSchedule.StatusDetails = _loc.T("ExpiredMessage");
            ActiveSchedule = null;
            _conditions.EndSchedule();
            _timer.Stop();
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
            Error?.Invoke(this, _loc.T("ExpiredMessage"));
            return;
        }

        if (now >= ActiveSchedule.TargetTime.AddMinutes(-2) && now < ActiveSchedule.TargetTime && !ActiveSchedule.WarningShown)
        {
            ActiveSchedule.WarningShown = true;
            ActiveSchedule.Status = ScheduleStatus.WarningShown;
            WarningDue?.Invoke(this, ActiveSchedule);
        }

        if (!ActiveSchedule.FiveMinuteAlertShown && now >= ActiveSchedule.TargetTime.AddMinutes(-5) && now < ActiveSchedule.TargetTime)
        {
            ActiveSchedule.FiveMinuteAlertShown = true;
            AlertDue?.Invoke(this, new ScheduleAlert(ScheduleAlertKind.FiveMinutesBefore, ActiveSchedule));
        }

        if (!ActiveSchedule.OneMinuteAlertShown && now >= ActiveSchedule.TargetTime.AddMinutes(-1) && now < ActiveSchedule.TargetTime)
        {
            ActiveSchedule.OneMinuteAlertShown = true;
            AlertDue?.Invoke(this, new ScheduleAlert(ScheduleAlertKind.OneMinuteBefore, ActiveSchedule));
        }

        if (now < ActiveSchedule.TargetTime) return;

        var conditionResult = _conditions.Evaluate(ActiveSchedule.Request);
        if (!conditionResult.IsSatisfied)
        {
            // Conditions must hold for the whole grace minute; if they drop, the countdown starts over later.
            ActiveSchedule.ExecutionPendingUntil = null;
            ActiveSchedule.Status = ScheduleStatus.WaitingForConditions;
            ActiveSchedule.StatusDetails = conditionResult.Message;
            return;
        }

        if (ActiveSchedule.ExecutionPendingUntil is not { } pendingUntil)
        {
            ActiveSchedule.ExecutionPendingUntil = now.AddMinutes(1);
            ActiveSchedule.Status = ScheduleStatus.ConditionsMet;
            ActiveSchedule.StatusDetails = string.Format(_loc.T("ConditionsMetDetails"), RemainingTimeFormatter.FormatMinutesSeconds(ActiveSchedule.ExecutionPendingUntil.Value - now));
            AlertDue?.Invoke(this, new ScheduleAlert(ScheduleAlertKind.ConditionsMet, ActiveSchedule));
            return;
        }

        if (now < pendingUntil)
        {
            ActiveSchedule.StatusDetails = string.Format(_loc.T("ConditionsMetDetails"), RemainingTimeFormatter.FormatMinutesSeconds(pendingUntil - now));
            return;
        }

        ActiveSchedule.ExecutionPendingUntil = null;
        ActiveSchedule.Status = ScheduleStatus.Executing;
        try
        {
            await _executor.ExecuteAsync(ActiveSchedule.Request);
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, $"{_loc.T("ExecuteError")} {ex.Message}");
        }

        if (ActiveSchedule?.IsRecurring == true)
        {
            ActiveSchedule.TargetTime = GetNextRecurringTime(ActiveSchedule);
            ActiveSchedule.WarningShown = false;
            MarkAlertsAlreadyPassed(ActiveSchedule, DateTime.Now);
            ActiveSchedule.Status = ScheduleStatus.Scheduled;
            ActiveSchedule.StatusDetails = string.Empty;
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ActiveSchedule = null;
            _conditions.EndSchedule();
            _timer.Stop();
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // A cue whose moment has already passed (e.g. scheduling 3 minutes ahead) is skipped instead of firing at once.
    private static void MarkAlertsAlreadyPassed(ActiveSchedule schedule, DateTime now)
    {
        schedule.FiveMinuteAlertShown = now >= schedule.TargetTime.AddMinutes(-5);
        schedule.OneMinuteAlertShown = now >= schedule.TargetTime.AddMinutes(-1);
    }

    private static DateTime GetNextRecurringTime(ActiveSchedule schedule)
    {
        if (schedule.Request.ScheduleMode == ScheduleMode.Daily)
        {
            return schedule.TargetTime.AddDays(1);
        }

        var next = schedule.TargetTime.AddDays(1);
        while (next.DayOfWeek != schedule.Request.WeeklyDay)
        {
            next = next.AddDays(1);
        }

        return next;
    }
}
