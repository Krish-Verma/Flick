using Xunit;

namespace LocalClip.Core.Tests;

/// <summary>
/// Opt-in real FFmpeg workflows, separate from device-dependent recording.
/// An enabled gate fails for missing tools; it never silently skips them.
/// </summary>
public sealed class MediaFactAttribute : FactAttribute
{
    public MediaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FLICK_MEDIA_TESTS") != "1")
            Skip = "Requires Windows, pinned FFmpeg/ffprobe, and explicit FLICK_MEDIA_TESTS=1; uses generated media only";
    }
}
