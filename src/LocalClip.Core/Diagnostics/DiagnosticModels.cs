namespace LocalClip.Core.Diagnostics;

public enum DiagnosticEventKind { Unknown, ApplicationStarted, EngineChanged, HostChanged, CaptureConfigured, SaveRequested, SaveAccepted, SaveValidated, SaveFailed, RecordingFailed, HotkeyChanged, DiagnosticTestCompleted }
public enum DiagnosticErrorCode { None, Unknown, MissingSource, StartupFailed, StartupRefused, EngineFault, TargetLost, SaveTimedOut, HostUnresponsive, HostExited, StorageUnavailable, InvalidMedia, IndexWriteFailed, HotkeyConflict, NotReady, CommandRejected, ForcedStop, EncoderUnavailable, DependencyMissing }
public enum DiagnosticSourceKind { Unknown, Monitor, Window }
public enum DiagnosticEncoder { Unknown, NvidiaTexture, NvidiaCompatibility, IntelTexture, IntelCompatibility, IntelLegacy, IntelLegacyCompatibility, AmdTexture, AmdCompatibility, CpuX264, NvidiaFfmpeg, NvidiaLegacy }
public enum DiagnosticHotkeyAction { Unknown, ToggleReplay, SaveReplay }
public enum DiagnosticReleaseChannel { Release, Preview, Beta, ReleaseCandidate }
public enum DiagnosticStorageState { Ready, Unavailable, Malformed, Disposed }
public enum DiagnosticStorageIssue { None, AccessDenied, InvalidLocation, InvalidData, TooLarge, UnsupportedVersion, ExternalModification, WriteFailed, Disposed }
public enum DiagnosticFlushResult { Persisted, StorageUnavailable, Disposed }
public enum DiagnosticExportError { None, InvalidDestination, AlreadyExists, AccessDenied, IoFailure, Disposed }

/// <summary>No arbitrary prerelease text or build metadata can enter the journal.</summary>
public sealed record DiagnosticAppVersion(int Major, int Minor, int Patch,
    DiagnosticReleaseChannel Channel = DiagnosticReleaseChannel.Release, int ChannelNumber = 0)
{
    public static bool TryParse(string? value, out DiagnosticAppVersion version)
    {
        version = new(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
        string[] pieces = value.Split('-');
        if (pieces.Length > 2) return false;
        string[] numbers = pieces[0].Split('.');
        if (numbers.Length != 3 || !ParseNumber(numbers[0], out int major) ||
            !ParseNumber(numbers[1], out int minor) || !ParseNumber(numbers[2], out int patch)) return false;
        var channel = DiagnosticReleaseChannel.Release;
        int channelNumber = 0;
        if (pieces.Length == 2)
        {
            string[] suffix = pieces[1].Split('.');
            if (suffix.Length != 2 || !ParseNumber(suffix[1], out channelNumber)) return false;
            channel = suffix[0] switch { "preview" => DiagnosticReleaseChannel.Preview,
                "beta" => DiagnosticReleaseChannel.Beta, "rc" => DiagnosticReleaseChannel.ReleaseCandidate, _ => (DiagnosticReleaseChannel)(-1) };
            if (!Enum.IsDefined(channel)) return false;
        }
        version = new(major, minor, patch, channel, channelNumber);
        return true;
    }

    internal bool IsValid => Major >= 0 && Minor >= 0 && Patch >= 0 && ChannelNumber >= 0
        && Enum.IsDefined(Channel) && (Channel != DiagnosticReleaseChannel.Release || ChannelNumber == 0);

    private static bool ParseNumber(string text, out int number)
    {
        number = 0;
        return text.Length is > 0 and <= 10 && (text.Length == 1 || text[0] != '0') &&
            text.All(c => c is >= '0' and <= '9') && int.TryParse(text, out number);
    }
}

/// <summary>Closed enums and bounded numerics only. Never add free-text/context fields.</summary>
public sealed record DiagnosticObservation(DiagnosticEventKind Kind,
    RecordingEngineState? EngineState = null, ReplaySaveState? SaveState = null,
    CaptureHostHealth? HostHealth = null, DiagnosticSourceKind SourceKind = DiagnosticSourceKind.Unknown,
    DiagnosticEncoder Encoder = DiagnosticEncoder.Unknown, DiagnosticErrorCode Error = DiagnosticErrorCode.None,
    DiagnosticHotkeyAction HotkeyAction = DiagnosticHotkeyAction.Unknown, bool? HotkeyRegistered = null,
    int? NativeError = null, int? Width = null, int? Height = null, int? Fps = null,
    long? BufferedMilliseconds = null, long? MaximumBufferMilliseconds = null,
    long? DurationMilliseconds = null, long? LatencyMilliseconds = null,
    long? MemoryBytes = null, bool? SoftwareFallback = null, bool? BufferEstimated = null,
    bool? BufferTrackingLimited = null)
{
    public static DiagnosticObservation FromHealth(DiagnosticEventKind kind,
        RecordingHealthSnapshot health, DiagnosticErrorCode error = DiagnosticErrorCode.None)
    {
        ArgumentNullException.ThrowIfNull(health);
        bool validated = kind == DiagnosticEventKind.SaveValidated && health.LastValidatedClip != null;
        var profile = validated ? health.LastValidatedClip!.Profile : health.Profile;
        var source = validated ? health.LastValidatedClip!.Source : health.Source;
        return new(kind, health.EngineState, health.SaveState, health.HostHealth,
            source?.Kind switch { "Monitor" => DiagnosticSourceKind.Monitor, "Window" => DiagnosticSourceKind.Window, _ => DiagnosticSourceKind.Unknown },
            DiagnosticPolicy.KnownEncoder(profile?.EncoderId), error,
            Width: profile?.Width, Height: profile?.Height, Fps: profile?.Fps,
            BufferedMilliseconds: DiagnosticPolicy.Milliseconds(health.BufferedSeconds),
            MaximumBufferMilliseconds: DiagnosticPolicy.Milliseconds(health.MaximumBufferSeconds),
            DurationMilliseconds: kind == DiagnosticEventKind.SaveValidated && health.LastValidatedClip != null
                ? DiagnosticPolicy.Milliseconds(health.LastValidatedClip.DurationSeconds) : null,
            SoftwareFallback: profile?.IsSoftwareFallback, BufferEstimated: health.BufferedDurationEstimated,
            BufferTrackingLimited: health.BufferTrackingLimited);
    }
}

public sealed record DiagnosticEntry(DateTimeOffset TimestampUtc, DiagnosticObservation Observation, DiagnosticAppVersion Version);
public sealed record DiagnosticDocument(int SchemaVersion, DiagnosticAppVersion Version,
    DiagnosticStorageState StorageState, long DroppedEvents, DiagnosticEntry[] Events);
public sealed record DiagnosticJournalStatus(DiagnosticStorageState StorageState,
    DiagnosticStorageIssue StorageIssue, int RetainedEvents, long Revision,
    long PersistedRevision, long DroppedEvents, bool PendingWrite);
public sealed record DiagnosticExportResult(bool Success, DiagnosticExportError Error, long Bytes = 0, int EventCount = 0);
