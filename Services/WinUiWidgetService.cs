using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Persistence;
using Shutdown_Scheduler_WinUI.Views;

namespace Shutdown_Scheduler.Services;

public sealed class WinUiWidgetService : IWidgetService, IDisposable
{
    private readonly WidgetSettingsStore _store;
    private readonly LocalizationService _loc;
    private readonly DispatcherQueue _dispatcherQueue;
    private WidgetWindow? _window;
    private ActiveSchedule? _lastSchedule;

    public WinUiWidgetService(WidgetSettingsStore store, LocalizationService loc, DispatcherQueue dispatcherQueue)
    {
        _store = store;
        _loc = loc;
        _dispatcherQueue = dispatcherQueue;
        Settings = _store.Load();
    }

    public WidgetSettings Settings { get; }

    public void ApplySettings()
    {
        _store.Save(Settings);
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!Settings.Enabled)
            {
                HideWindow();
                return;
            }

            if (_lastSchedule is not null)
            {
                EnsureWindow();
                _window?.ApplySettings(Settings);
                UpdateWindowText(_lastSchedule);
            }
        });
    }

    public void Update(ActiveSchedule? schedule)
    {
        _lastSchedule = schedule;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!Settings.Enabled || schedule is null)
            {
                if (schedule is null)
                {
                    CloseWindow();
                }
                else
                {
                    HideWindow();
                }

                _store.Save(Settings);
                return;
            }

            EnsureWindow();
            _window?.ApplySettings(Settings);
            UpdateWindowText(schedule);
            _window?.ShowWidget();
            _store.Save(Settings);
        });
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new WidgetWindow();
        _window.Closed += (_, _) => _window = null;
        _window.ApplySettings(Settings);
        _window.Activate();
    }

    private void HideWindow()
    {
        _window?.HideWidget();
    }

    private void CloseWindow()
    {
        if (_window is null)
        {
            return;
        }

        var window = _window;
        _window = null;
        window.Close();
    }

    private void UpdateWindowText(ActiveSchedule schedule)
    {
        if (_window is null)
        {
            return;
        }

        var actionText = _loc.ActionLabel(schedule.Request.Action);
        var remaining = schedule.TargetTime - DateTime.Now;
        var timeText = Settings.DisplayMode == WidgetDisplayMode.ExecutionTime
            ? schedule.TargetTime.ToString("HH:mm")
            : RemainingTimeFormatter.FormatHoursMinutesSeconds(remaining);
        var conditionIcons = GetConditionIcons(schedule.Request);
        _window.SetScheduleText(actionText, AppGlyphs.ForAction(schedule.Request.Action), timeText, conditionIcons);
    }

    private static string GetConditionIcons(ScheduleRequest request)
    {
        var icons = new List<string>(4);
        if (request.NetworkConditionEnabled) icons.Add(AppGlyphs.ForCondition(ConditionType.Network));
        if (request.CpuConditionEnabled) icons.Add(AppGlyphs.ForCondition(ConditionType.Cpu));
        if (request.BatteryConditionEnabled) icons.Add(AppGlyphs.ForCondition(ConditionType.Battery));
        if (request.AppIdleConditionEnabled) icons.Add(AppGlyphs.ForCondition(ConditionType.AppIdle));
        return string.Join('|', icons);
    }

    public void Dispose()
    {
        CloseWindow();
    }
}
