using System;
using System.Collections.Generic;

namespace LocalClip.Core.Rules;

/// <summary>
/// Decimal MB upload budgets. The default is 20 MB, not MiB. All targets
/// are local; actual destination/account upload allowances may be smaller.
/// </summary>
public static class ShareExportTargets
{
    public const int DefaultMb = 20;
    public const int MinimumMb = 1;
    public const int MaximumMb = 250;

    public static IReadOnlyList<int> Presets { get; } = Array.AsReadOnly(new[] { 10, 20, 50, 100 });

    public static bool IsValid(int megabytes) =>
        megabytes >= MinimumMb && megabytes <= MaximumMb;

    public static bool IsPreset(int megabytes) =>
        megabytes is 10 or 20 or 50 or 100;

    public static long ToBytes(int megabytes)
    {
        if (!IsValid(megabytes))
            throw new ArgumentOutOfRangeException(nameof(megabytes),
                "Choose a file-size cap between 1 and 250 decimal MB.");
        return megabytes * 1_000_000L;
    }
}
