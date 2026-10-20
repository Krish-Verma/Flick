using System;
using System.Globalization;

namespace LocalClip.Core.Rules;

/// <summary>
/// Small, UI-independent trim editing contract. Coordinates/keyboard/numeric
/// input all use the same finite, bounded range as FFmpeg validation.
/// </summary>
public static class TrimTimelineMath
{
    public const double MinSelectionSeconds = 0.25;
    public const double KeyboardFineStep = 0.1;
    public const double KeyboardCoarseStep = 1.0;

    public static bool TryCreate(double originalDuration, double proposedStart,
        double proposedEnd, out double start, out double end)
    {
        start = end = 0;
        if (!double.IsFinite(originalDuration) || !double.IsFinite(proposedStart) ||
            !double.IsFinite(proposedEnd) || originalDuration < MinSelectionSeconds)
            return false;

        start = Math.Clamp(proposedStart, 0, originalDuration - MinSelectionSeconds);
        end = Math.Clamp(proposedEnd <= 0 ? originalDuration : proposedEnd,
            start + MinSelectionSeconds, originalDuration);
        return double.IsFinite(end - start) && end - start >= MinSelectionSeconds - 1e-8;
    }

    public static double MoveStart(double proposedStart, double end, double originalDuration)
    {
        if (!double.IsFinite(proposedStart) || !double.IsFinite(end) ||
            !double.IsFinite(originalDuration) || originalDuration < MinSelectionSeconds)
            throw new ArgumentOutOfRangeException(nameof(originalDuration));
        return Math.Clamp(proposedStart, 0, Math.Max(0, Math.Min(end, originalDuration) - MinSelectionSeconds));
    }

    public static double MoveEnd(double proposedEnd, double start, double originalDuration)
    {
        if (!double.IsFinite(proposedEnd) || !double.IsFinite(start) ||
            !double.IsFinite(originalDuration) || originalDuration < MinSelectionSeconds)
            throw new ArgumentOutOfRangeException(nameof(originalDuration));
        return Math.Clamp(proposedEnd, Math.Min(originalDuration, start + MinSelectionSeconds), originalDuration);
    }

    /// <summary>Accept seconds or M:SS[.fff] / H:MM:SS[.fff], invariant culture.</summary>
    public static bool TryParseTime(string? input, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        string[] parts = input.Trim().Split(':');
        if (parts.Length > 3) return false;
        double value = 0;
        foreach (string part in parts)
        {
            if (!double.TryParse(part.Trim(), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double segment) ||
                !double.IsFinite(segment) || segment < 0) return false;
            if (parts.Length > 1 && !ReferenceEquals(part, parts[0]) && segment >= 60)
                return false;
            value = value * 60 + segment;
        }
        seconds = value;
        return double.IsFinite(seconds);
    }

    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "0:00.00";
        int hours = (int)Math.Floor(seconds / 3600);
        int minutes = (int)Math.Floor((seconds - hours * 3600) / 60);
        double secs = seconds - hours * 3600 - minutes * 60;
        // Avoid displaying :60.00 after rounding to two decimals.
        var rounded = TimeSpan.FromSeconds(Math.Round(seconds, 2, MidpointRounding.AwayFromZero));
        return rounded.TotalHours >= 1
            ? rounded.ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture)
            : rounded.ToString(@"m\:ss\.ff", CultureInfo.InvariantCulture);
    }
}
