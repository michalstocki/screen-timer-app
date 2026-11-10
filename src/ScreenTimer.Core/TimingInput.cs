using System.Globalization;

namespace ScreenTimer.Core;

/// <summary>Parses and validates the timing fields typed in the settings window.</summary>
public static class TimingInput
{
    public const double MaxMinutes = 24 * 60;
    public const int MaxExtensionMinutes = 240;

    /// <summary>Applies the typed values to <paramref name="config"/>; on failure returns a message and leaves it untouched.</summary>
    public static bool TryApply(AppConfig config, string session, string breakTime, string warning,
        string extensions, out string error)
    {
        if (!TryMinutes(session, 1, MaxMinutes, out var sessionMin))
            return Fail($"Czas sesji musi być liczbą minut od 1 do {MaxMinutes:0}.", out error);
        if (!TryMinutes(breakTime, 1, MaxMinutes, out var breakMin))
            return Fail($"Czas przerwy musi być liczbą minut od 1 do {MaxMinutes:0}.", out error);
        if (!TryMinutes(warning, 0, MaxMinutes, out var warningMin))
            return Fail("Ostrzeżenie musi być liczbą minut (0 wyłącza ostrzeżenie).", out error);
        if (warningMin > sessionMin)
            return Fail("Ostrzeżenie nie może być dłuższe niż sesja.", out error);
        if (!TryExtensions(extensions, out var extensionMin))
            return Fail($"Przedłużenia to minuty od 1 do {MaxExtensionMinutes} oddzielone przecinkami, np. 5, 10, 15.", out error);

        config.SessionMinutes = sessionMin;
        config.BreakMinutes = breakMin;
        config.WarningMinutes = warningMin;
        config.ExtensionMinutes = extensionMin;
        error = "";
        return true;
    }

    public static string FormatMinutes(double minutes) =>
        minutes.ToString("0.##", CultureInfo.CurrentCulture);

    public static string FormatExtensions(IEnumerable<int> minutes) => string.Join(", ", minutes);

    static bool TryMinutes(string text, double min, double max, out double value)
    {
        // Accept both "7,5" and "7.5" regardless of the Windows regional settings.
        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value) && value >= min && value <= max;
    }

    static bool TryExtensions(string text, out int[] minutes)
    {
        var parts = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        var parsed = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var m)
                || m < 1 || m > MaxExtensionMinutes)
            {
                minutes = [];
                return false;
            }
            parsed.Add(m);
        }

        minutes = parsed.Distinct().Order().ToArray();
        return minutes.Length > 0;
    }

    static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
