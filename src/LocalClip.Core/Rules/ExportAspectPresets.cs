namespace LocalClip.Core.Rules;

public static class ExportAspectPresets
{
    public const string Original = "Original", Vertical9x16 = "Vertical9x16", Square1x1 = "Square1x1";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Original, Vertical9x16, Square1x1 });
    public static bool IsValid(string? value) => value is Original or Vertical9x16 or Square1x1;
    public static string Label(string value) => value switch
    { Vertical9x16 => "Fit 9:16", Square1x1 => "Fit 1:1", _ => "Original aspect" };
    public static (int Width, int Height) Canvas(string preset, int maximumWidth, int maximumHeight)
    {
        if (!IsValid(preset)) throw new ArgumentException("Choose a supported export aspect preset.");
        if (maximumWidth < 2 || maximumHeight < 2) throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        if (preset == Original) return (maximumWidth & ~1, maximumHeight & ~1);
        if (preset == Square1x1)
        { int edge = Math.Max(2, Math.Min(maximumWidth, maximumHeight) & ~1); return (edge, edge); }
        // Multiples of 18×32 maintain exact 9:16 and even YUV420 geometry.
        int multiplier = Math.Min(maximumWidth / 18, maximumHeight / 32);
        if (multiplier < 1) throw new ArgumentException("The source is too small for a bounded 9:16 export.");
        return (18 * multiplier, 32 * multiplier);
    }
    public static string VideoFilter(string preset, int width, int height)
    {
        string fit = $"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2:reset_sar=1";
        return preset == Original ? fit + ",setsar=1,format=yuv420p" :
            fit + $",pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,format=yuv420p";
    }
    public static bool MatchesDimensions(string preset, int width, int height) => width > 0 && height > 0 &&
        width % 2 == 0 && height % 2 == 0 && (preset switch
        { Vertical9x16 => width * 16L == height * 9L, Square1x1 => width == height, Original => true, _ => false });
}
