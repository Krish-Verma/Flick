using LocalClip.Core.Ipc;

namespace LocalClip.Core.Diagnostics;

public enum RecordingEngineState { Off, Starting, Buffering, Ready, Stopping, Stopped, Faulted }
public enum ReplaySaveState { Idle, Requested, Accepted, Validating, Succeeded, Failed }
public enum CaptureHostHealth { Stopped, Starting, Responsive, Busy, Unresponsive, Exited }

/// <summary>Local UI provenance. It must not be copied into diagnostic exports without redaction.</summary>
public sealed record RecordingSource(string Kind, string Name, string Identity = "");

/// <summary>Retained local error details; Message may contain private paths or native error text.</summary>
public sealed record RecordingIssue(string Code, string Message, DateTimeOffset When,
    Guid SessionId = default, string? SaveId = null);

public sealed record RecordingHotkeyHealth(string Action, string RequestedChord,
    string? RegisteredChord, bool IsRegistered, int? Win32Error = null, string Message = "");

public sealed record SavedClipSummary(string ClipId, string SaveId, Guid SessionId,
    double DurationSeconds, DateTimeOffset SavedAt, RecordingSource Source, CaptureEncodingProfile? Profile);

/// <summary>
/// A point-in-time view. Engine activity and save activity are independent:
/// stopping or restarting capture does not settle a previously accepted save.
/// </summary>
public sealed record RecordingHealthSnapshot(
    long Revision,
    Guid SessionId,
    RecordingEngineState EngineState,
    ReplaySaveState SaveState,
    int PendingSaveCount,
    RecordingSource? Source,
    CaptureEncodingProfile? Profile,
    CaptureEncodingProfile? LastProfile,
    CaptureHostHealth HostHealth,
    double BufferedSeconds,
    double MaximumBufferSeconds,
    bool BufferedDurationEstimated,
    bool BufferTrackingLimited,
    RecordingIssue? LastRecordingIssue,
    RecordingIssue? LastSaveIssue,
    SavedClipSummary? LastValidatedClip,
    IReadOnlyList<RecordingHotkeyHealth> Hotkeys)
{
    // Missing command acknowledgement is uncertainty, not a terminal failure.
    // Kept separate from SaveState so native completion/validation ordering is unchanged.
    public bool SaveAcknowledgementUnconfirmed { get; init; }

    public string StatusText => EngineState == RecordingEngineState.Faulted ? "Recording faulted"
        : PendingSaveCount > 0 ? "Saving clip"
        : HostHealth == CaptureHostHealth.Unresponsive ? "Recording status unavailable"
        : SaveState == ReplaySaveState.Succeeded ? "Save successful"
        : SaveState == ReplaySaveState.Failed ? "Save failed"
        : EngineState switch
        {
            RecordingEngineState.Starting => "Starting",
            RecordingEngineState.Buffering => "Buffering",
            RecordingEngineState.Ready => "Replay ready",
            RecordingEngineState.Stopping => "Recording stopping",
            RecordingEngineState.Stopped => "Recording stopped",
            _ => "Replay off"
        };
}
