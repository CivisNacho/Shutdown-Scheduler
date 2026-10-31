using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Persistence;
using Shutdown_Scheduler_WinUI.Views;

namespace Shutdown_Scheduler.Services;

public sealed class WinUiWidgetService : IWidgetService, IDisposable
{
    private readonly WidgetSettingsStore _store;
    private readonly LocalizationService _loc;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly List<WidgetWindow> _windows = [];
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
                HideWindows();
                return;
            }

            if (_lastSchedule is not null)
            {
                SyncWindows();
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
                    CloseWindows();
                }
                else
                {
                    HideWindows();
                }

                _store.Save(Settings);
                return;
            }

            SyncWindows();
            UpdateWindowText(schedule);
            ShowWindows();
            _store.Save(Settings);
        });
    }

    // Keeps one widget per target display: every display when ShowOnAllScreens is on, otherwise a single one on the primary display.
    private void SyncWindows()
    {
        var displays = Settings.ShowOnAllScreens ? DisplayArea.FindAll().ToArray() : Array.Empty<DisplayArea>();
        var targetCount = Math.Max(1, displays.Length);

        while (_windows.Count > targetCount)
        {
            var last = _windows[^1];
            _windows.RemoveAt(_windows.Count - 1);
            last.Close();
        }

        while (_windows.Count < targetCount)
        {
            var window = new WidgetWindow();
            window.Closed += (_, _) => _windows.Remove(window);
            _windows.Add(window);
            window.Activate();
        }

        for (var i = 0; i < _windows.Count; i++)
        {
            _windows[i].ApplySettings(Settings, displays.Length > 0 ? displays[i] : null);
        }
    }

    private void ShowWindows()
    {
        foreach (var window in _windows.ToArray())
        {
            window.ShowWidget();
        }
    }

    private void HideWindows()
    {
        foreach (var window in _windows.ToArray())
        {
            window.HideWidget();
        }
    }

    private void CloseWindows()
    {
        var windows = _windows.ToArray();
        _windows.Clear();
        foreach (var window in windows)
        {
            window.Close();
        }
    }

    private void UpdateWindowText(ActiveSchedule schedule)
    {
        var actionText = _loc.ActionLabel(schedule.Request.Action);
        var remaining = schedule.TargetTime - DateTime.Now;
        var timeText = Settings.DisplayMode == WidgetDisplayMode.ExecutionTime
            ? schedule.TargetTime.ToString("HH:mm")
            : RemainingTimeFormatter.FormatHoursMinutesSeconds(remaining);
        var conditionIcons = GetConditionIcons(schedule.Request);
        var actionGlyph = AppGlyphs.ForAction(schedule.Request.Action);
        foreach (var window in _windows)
        {
            window.SetScheduleText(actionText, actionGlyph, timeText, conditionIcons);
        }
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
        CloseWindows();
    }
}
