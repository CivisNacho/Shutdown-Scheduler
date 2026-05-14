using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler.Services;

public interface IWidgetService
{
    WidgetSettings Settings { get; }
    void ApplySettings();
    void Update(ActiveSchedule? schedule);
}

