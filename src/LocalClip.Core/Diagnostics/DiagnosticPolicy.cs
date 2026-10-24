namespace LocalClip.Core.Diagnostics;

public static class DiagnosticPolicy
{
    public const int MaximumEvents = 256;
    public const int MaximumBytes = 256 * 1024;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public static DiagnosticEncoder KnownEncoder(string? id) => id switch
    {
        "obs_nvenc_h264_tex" => DiagnosticEncoder.NvidiaTexture,
        "obs_nvenc_h264_soft" => DiagnosticEncoder.NvidiaCompatibility,
        "ffmpeg_nvenc" => DiagnosticEncoder.NvidiaFfmpeg,
        "jim_nvenc" => DiagnosticEncoder.NvidiaLegacy,
        "obs_qsv11_v2" => DiagnosticEncoder.IntelTexture,
        "obs_qsv11_soft_v2" => DiagnosticEncoder.IntelCompatibility,
        "obs_qsv11" => DiagnosticEncoder.IntelLegacy,
        "obs_qsv11_soft" => DiagnosticEncoder.IntelLegacyCompatibility,
        "h264_texture_amf" => DiagnosticEncoder.AmdTexture,
        "h264_fallback_amf" => DiagnosticEncoder.AmdCompatibility,
        "obs_x264" => DiagnosticEncoder.CpuX264,
        _ => DiagnosticEncoder.Unknown
    };

    internal static long? Milliseconds(double seconds) => double.IsFinite(seconds) && seconds >= 0 && seconds <= 600
        ? (long)Math.Round(seconds * 1000) : null;

    public static bool IsValid(DiagnosticObservation? item) => item != null &&
        Enum.IsDefined(item.Kind) && item.Kind != DiagnosticEventKind.Unknown &&
        Defined(item.EngineState) && Defined(item.SaveState) && Defined(item.HostHealth) &&
        Enum.IsDefined(item.SourceKind) && Enum.IsDefined(item.Encoder) && Enum.IsDefined(item.Error) && Enum.IsDefined(item.HotkeyAction) &&
        Range(item.NativeError, 0, int.MaxValue) && Range(item.Width, 1, 16384) && Range(item.Height, 1, 16384) &&
        Range(item.Fps, 1, 1000) && Range(item.BufferedMilliseconds, 0, 600000) &&
        Range(item.MaximumBufferMilliseconds, 0, 600000) && Range(item.DurationMilliseconds, 0, 600000) &&
        Range(item.LatencyMilliseconds, 0, 3600000) && Range(item.MemoryBytes, 0, long.MaxValue);

    private static bool Range(long? value, long minimum, long maximum) => value == null || value >= minimum && value <= maximum;
    private static bool Defined<T>(T? value) where T : struct, Enum => value == null || Enum.IsDefined(value.Value);
}
