using System;
using LocalClip.Core.Models;

namespace LocalClip.Core.Rules;

/// <summary>
/// Strictly restore the user's last capture source. A missing window never
/// falls back to recording the desktop, and recycled window handles alone
/// cannot establish that the user authorized the current window.
/// </summary>
public static class CaptureTargetSelectionRules
{
    public static bool MatchesSavedPreference(
        AppSettings saved, string candidateType, int candidateMonitorIndex,
        string? candidateWindowTitle, string? candidateWindowExecutable)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (string.Equals(saved.TargetType, "Monitor", StringComparison.Ordinal))
            return string.Equals(candidateType, "Monitor", StringComparison.Ordinal)
                && candidateMonitorIndex == saved.MonitorIndex;

        if (!string.Equals(saved.TargetType, "Window", StringComparison.Ordinal) ||
            !string.Equals(candidateType, "Window", StringComparison.Ordinal))
            return false;

        // Saved HWNDs are not stable security identifiers across restarts.
        // Refuse ambiguous/missing identity instead of choosing a monitor.
        return !string.IsNullOrWhiteSpace(saved.WindowTitle)
            && !string.IsNullOrWhiteSpace(saved.WindowExecutable)
            && string.Equals(candidateWindowTitle, saved.WindowTitle, StringComparison.Ordinal)
            && string.Equals(candidateWindowExecutable, saved.WindowExecutable,
                StringComparison.OrdinalIgnoreCase);
    }
}
