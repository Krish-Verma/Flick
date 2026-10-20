using System;

namespace LocalClip.Core.Rules;

/// <summary>
/// Fixed preview budget: never automatically transcode a large imported library.
/// Only requested, bounded copies of valid video are permitted.
/// </summary>
public static class PlaybackPreviewPolicy
{
    public const double MaximumSeconds = 180;
    public const long MaximumOutputBytes = 120_000_000L;
    public const long MaximumCopySourceBytes = 100_000_000L;

    public static double PreviewSeconds(double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0)
            throw new ArgumentOutOfRangeException(nameof(duration));
        return Math.Min(duration, MaximumSeconds);
    }

    public static bool CanStreamCopy(string codec, double duration, long sourceBytes) =>
        string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase) &&
        double.IsFinite(duration) &&
        duration > 0 && duration <= MaximumSeconds &&
        sourceBytes > 0 && sourceBytes <= MaximumCopySourceBytes;
}
