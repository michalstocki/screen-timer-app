using System;

namespace ScreenTimer.Ui;

internal static class TimeFormat
{
    /// <summary>mm:ss, or h:mm:ss for an hour or more. Rounds up so 0:00 only shows at the very end.</summary>
    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, seconds)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
