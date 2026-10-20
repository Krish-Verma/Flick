using System;
using Xunit;

namespace LocalClip.Core.Tests;

/// <summary>
/// Explicit, independently visible hardware gate. No missing binary or GPU test
/// can quietly return a green result. CI reports these as skipped.
/// </summary>
public sealed class HardwareFactAttribute : FactAttribute
{
    public HardwareFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FLICK_HARDWARE_TESTS") != "1")
            Skip = "Requires Windows GPU, native OBS runtime, and explicit FLICK_HARDWARE_TESTS=1";
    }
}
