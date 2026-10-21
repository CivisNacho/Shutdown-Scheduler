namespace Shutdown_Scheduler.Models;

// Segoe Fluent Icons / MDL2 glyphs shared by the main window and the floating widget.
public static class AppGlyphs
{
    public const string NoSchedule = ""; // Clock
    public const string Schedule = ""; // Calendar
    public const string Conditions = ""; // Filter
    public const string Widget = ""; // Window
    public const string Add = "";
    public const string Remove = ""; // Delete
    public const string Refresh = "";

    public static string ForAction(PowerAction action)
        => action switch
        {
            PowerAction.Restart => "", // Refresh
            PowerAction.Suspend => "", // Moon
            PowerAction.Hibernate => "", // Clock
            PowerAction.LogOff => "", // Switch user
            PowerAction.LockSession => "", // Lock
            PowerAction.CloseApp => "", // Close
            _ => "" // Power
        };

    public static string ForCondition(ConditionType type)
        => type switch
        {
            ConditionType.Network => "", // Wi-Fi
            ConditionType.Cpu => "", // CPU-like glyph
            ConditionType.Battery => "", // Battery
            _ => "" // Stopwatch (app idle)
        };
}
