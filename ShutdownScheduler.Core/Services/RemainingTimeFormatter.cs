namespace Shutdown_Scheduler.Services;

public static class RemainingTimeFormatter
{
    public static string FormatHoursMinutesSeconds(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    public static string FormatMinutesSeconds(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}

