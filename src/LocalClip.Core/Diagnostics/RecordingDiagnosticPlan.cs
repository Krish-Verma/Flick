using LocalClip.Core.Models;

namespace LocalClip.Core.Diagnostics;

/// <summary>An immutable capture-only snapshot. Constructing a consent plan does no I/O and saves no preferences.</summary>
public sealed class RecordingDiagnosticPlan
{
    private readonly int _width, _height, _fps, _bitrate, _replaySeconds, _bufferMiB;
    private readonly string _encoder, _systemDeviceId, _microphoneDeviceId;
    private readonly bool _allowSoftwareFallback;

    public Guid PlanId { get; } = Guid.NewGuid();
    public RecordingDiagnosticTarget Target { get; }
    public RecordingSource Source { get; }
    public string OriginalsRoot { get; }
    public bool EnableSystemAudio { get; }
    public bool EnableMicrophone { get; }
    public int ReplayDurationSeconds => _replaySeconds;
    public string AudioSummary =>
        $"Computer audio: {(EnableSystemAudio ? "all audio on the selected output (" + _systemDeviceId + ")" : "Off")}. " +
        $"Microphone: {(EnableMicrophone ? "On (" + _microphoneDeviceId + ")" : "Off")}.";
    public string ConsentSummary => $"Record a short test of {Target.Name}.\n{AudioSummary}\nSave the test clip in your Flick library, then stop the test helper.";

    private RecordingDiagnosticPlan(AppSettings preferences, RecordingDiagnosticTarget target,
        bool enableSystemAudio, bool enableMicrophone)
    {
        Target = target with { };
        Source = new(target.Kind, target.Name, target.Kind == "Window" ? target.WindowExecutable : target.MonitorId);
        OriginalsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(preferences.OriginalsFolder));
        EnableSystemAudio = enableSystemAudio;
        EnableMicrophone = enableMicrophone;
        _width = preferences.VideoWidth;
        _height = preferences.VideoHeight;
        _fps = preferences.VideoFps;
        _bitrate = preferences.VideoBitrateKbps;
        _encoder = preferences.VideoEncoder;
        _allowSoftwareFallback = preferences.AllowSoftwareFallback;
        _replaySeconds = preferences.ReplayDurationSeconds;
        _bufferMiB = preferences.BufferMemoryLimitMb;
        _systemDeviceId = preferences.SystemAudioDeviceId;
        _microphoneDeviceId = preferences.MicrophoneDeviceId;
    }

    public static RecordingDiagnosticPlan Create(AppSettings preferences, RecordingDiagnosticTarget target,
        bool enableSystemAudio = false, bool enableMicrophone = false)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.Name)) throw new ArgumentException("Choose an available capture source.", nameof(target));
        if (target.Kind == "Window")
        {
            if (target.WindowHwnd == 0 || target.WindowPid == 0 || string.IsNullOrWhiteSpace(target.WindowTitle) ||
                string.IsNullOrWhiteSpace(target.WindowExecutable))
                throw new ArgumentException("The selected window needs its current handle, process and exact identity.", nameof(target));
        }
        else if (target.Kind == "Monitor")
        {
            if (target.MonitorIndex < 0 || string.IsNullOrWhiteSpace(target.MonitorId))
                throw new ArgumentException("The selected display needs its current device identity.", nameof(target));
        }
        else throw new ArgumentException("Choose a window or display.", nameof(target));

        if (string.IsNullOrWhiteSpace(preferences.OriginalsFolder) || !Path.IsPathFullyQualified(preferences.OriginalsFolder))
            throw new ArgumentException("Choose a fully qualified originals folder before testing.", nameof(preferences));
        if (preferences.VideoWidth <= 0 || preferences.VideoHeight <= 0 || preferences.VideoFps <= 0 ||
            preferences.VideoBitrateKbps <= 0 || string.IsNullOrWhiteSpace(preferences.VideoEncoder) ||
            preferences.ReplayDurationSeconds is < 1 or > 300 || preferences.BufferMemoryLimitMb is < 16 or > 2048)
            throw new ArgumentException("Review the capture profile before testing.", nameof(preferences));
        if (enableSystemAudio && string.IsNullOrWhiteSpace(preferences.SystemAudioDeviceId) ||
            enableMicrophone && string.IsNullOrWhiteSpace(preferences.MicrophoneDeviceId))
            throw new ArgumentException("Select the audio device before enabling it for the test.", nameof(preferences));
        return new(preferences, target, enableSystemAudio, enableMicrophone);
    }

    /// <summary>A new adapter-owned configuration, without credentials or a reference to the live preferences.</summary>
    internal AppSettings CreateCaptureSettings(string outputDirectory) => new()
    {
        TargetType = Target.Kind,
        MonitorIndex = Target.MonitorIndex,
        WindowHwnd = Target.WindowHwnd,
        WindowTitle = Target.WindowTitle,
        WindowExecutable = Target.WindowExecutable,
        VideoWidth = _width,
        VideoHeight = _height,
        VideoFps = _fps,
        VideoBitrateKbps = _bitrate,
        VideoEncoder = _encoder,
        AllowSoftwareFallback = _allowSoftwareFallback,
        ReplayDurationSeconds = _replaySeconds,
        BufferMemoryLimitMb = _bufferMiB,
        OriginalsFolder = outputDirectory,
        AudioMode = "SystemFallback",
        EnableMicrophone = EnableMicrophone,
        SystemAudioDeviceId = _systemDeviceId,
        MicrophoneDeviceId = _microphoneDeviceId
    };
}
