using System;

namespace LocalClip.Core.Rules;

public enum WorkspaceLayoutMode
{
    Split,
    Compact
}

/// <summary>
/// Pure display-size policy for the WPF shell. Switching modes changes only
/// layout; it never arms or stops the replay buffer or discards edits.
/// </summary>
public static class WorkspaceLayoutRules
{
    public const double CompactBreakpointDips = 1120;
    public const double MinimumWindowWidthDips = 720;
    public const double MinimumWindowHeightDips = 480;
    public const double SplitLibraryWidthDips = 286;
    public const double CompactSourceWidthDips = 188;
    public const double SplitSourceWidthDips = 260;

    public static WorkspaceLayoutMode Classify(double logicalWidth) =>
        double.IsFinite(logicalWidth) && logicalWidth >= CompactBreakpointDips
            ? WorkspaceLayoutMode.Split : WorkspaceLayoutMode.Compact;

    public static string SaveActionLabel(int replayDurationSeconds, bool compact)
    {
        if (replayDurationSeconds < 15 || replayDurationSeconds > 120)
            throw new ArgumentOutOfRangeException(nameof(replayDurationSeconds));
        return compact ? "Save Clip" : $"Save Last {replayDurationSeconds} Seconds";
    }
}
