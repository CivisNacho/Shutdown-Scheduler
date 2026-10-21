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

    // One size for every badge glyph, so the idle clock and the action glyphs look the same. The clock's ink is a
    // 0.938 em square anchored to the top-left of its em box, so it is nudged right/down by half its 0.062 em gap.
    public const double BadgeFontSize = 22.5;
    public const double ClockInkOffset = BadgeFontSize * 0.062 / 2; // ~0.7 px

    public static double FontSizeForAction(PowerAction action) => BadgeFontSize;

    // Offset (px) that centres a glyph's ink in its badge; only the clock glyph needs one.
    public static double InkOffsetFor(string glyph) => glyph == NoSchedule ? ClockInkOffset : 0;

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
