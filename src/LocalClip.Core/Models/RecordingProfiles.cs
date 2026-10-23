using System;

namespace LocalClip.Core.Models;

/// <summary>Requested quality profiles; encoder selection and CPU compatibility are independent preferences.</summary>
public static class RecordingProfiles
{
    public const string Light = "Light";
    public const string Balanced = "Balanced";
    public const string Smooth = "Smooth";

    public static bool TryApply(string preset, AppSettings settings)
    {
        (int width, int height, int fps, int kbps) profile = preset switch
        {
            Light => (1280, 720, 30, 6000),
            Balanced => (1920, 1080, 30, 15000),
            Smooth => (1920, 1080, 60, 20000),
            _ => default
        };
        if (profile.width == 0) return false;
        settings.QualityPreset = preset;
        settings.VideoWidth = profile.width;
        settings.VideoHeight = profile.height;
        settings.VideoFps = profile.fps;
        settings.VideoBitrateKbps = profile.kbps;
        return true;
    }

    public static string Identify(AppSettings settings)
    {
        if (settings.VideoWidth == 1280 && settings.VideoHeight == 720 &&
            settings.VideoFps == 30 && settings.VideoBitrateKbps == 6000) return Light;
        if (settings.VideoWidth == 1920 && settings.VideoHeight == 1080 &&
            settings.VideoFps == 60 && settings.VideoBitrateKbps == 20000) return Smooth;
        return Balanced;
    }

    // Estimate compressed packet usage, not total process RAM. Use warning only.
    public static long EstimatedReplayBytes(int durationSeconds, int videoBitrateKbps, bool microphone)
    {
        int audioKbps = microphone ? 384 : 256; // combined, system, optionally mic
        return (long)Math.Ceiling((videoBitrateKbps + audioKbps) * 1000d / 8d * durationSeconds * 1.15d);
    }
}
