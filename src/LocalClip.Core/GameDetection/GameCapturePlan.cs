using System.Text.Json;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;

namespace LocalClip.Core.GameDetection;

public sealed record GameCapturePlan(AppSettings Settings, CaptureStartOptions SourceOptions)
{
    /// <summary>A transient deep copy; never changes or saves manual preferences.</summary>
    public static GameCapturePlan Create(AppSettings manual, ApprovedGameProfile profile, GameWindowObservation window)
    {
        if (!GameDetectionPolicy.ValidateProfiles([profile], out _) || !profile.Enabled || !window.Eligible ||
            !string.Equals(profile.ExecutablePath.Replace('/', '\\'), window.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An eligible exact approved game window is required.");
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(manual))!;
        settings.TargetType = "Window"; settings.WindowHwnd = window.Hwnd;
        settings.WindowTitle = window.Title;
        settings.WindowExecutable = window.Process.ExecutablePath[(window.Process.ExecutablePath.LastIndexOf('\\') + 1)..];
        settings.ReplayDurationSeconds = profile.ReplayDurationSeconds;
        RecordingProfiles.TryApply(profile.QualityPreset, settings);
        settings.EnableMicrophone = manual.EnableMicrophone && profile.AllowMicrophone;
        return new(settings, new(ExpectedWindow: new(window.Hwnd, window.Process.Pid, window.Title, settings.WindowExecutable,
            window.Process.ExecutablePath, window.Process.CreationFileTime)));
    }
}
