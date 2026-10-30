using LocalClip.Core.Ipc;

namespace LocalClip.Core.Diagnostics;

/// <summary>
/// Thread-safe, in-memory recording observations. It launches no processes,
/// performs no I/O and retains only current health, outstanding saves and the
/// most recent issues/validated success. Callers own media validation and UI notifications.
/// </summary>
public sealed class RecordingHealthLedger
{
    private sealed record PendingSave(Guid SessionId, RecordingSource Source,
        CaptureEncodingProfile? Profile, ReplaySaveState State, bool AcknowledgementUnconfirmed = false);

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<string, PendingSave> _saves = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecordingHotkeyHealth> _hotkeys = new(StringComparer.Ordinal);
    private Guid _sessionId;
    private long _revision;
    private RecordingEngineState _engineState;
    private ReplaySaveState _saveOutcome;
    private RecordingSource? _source;
    private CaptureEncodingProfile? _profile;
    private CaptureEncodingProfile? _lastProfile;
    private CaptureHostHealth _hostHealth;
    private double _bufferedSeconds;
    private double _maximumBufferSeconds;
    private bool _estimated = true;
    private bool _trackingLimited;
    private bool _sessionEnded;
    private RecordingIssue? _lastRecordingIssue;
    private RecordingIssue? _lastSaveIssue;
    private SavedClipSummary? _lastValidatedClip;

    public RecordingHealthLedger(Func<DateTimeOffset>? utcNow = null) =>
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public RecordingHealthSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                // Never expose mutable dictionaries or a caller-owned collection.
                var hotkeys = Array.AsReadOnly(_hotkeys.Values.OrderBy(item => item.Action, StringComparer.Ordinal).ToArray());
                return new(_revision, _sessionId, _engineState, CurrentSaveState(), _saves.Count,
                    _source, _profile, _lastProfile, _hostHealth, _bufferedSeconds, _maximumBufferSeconds,
                    _estimated, _trackingLimited, _lastRecordingIssue, _lastSaveIssue, _lastValidatedClip, hotkeys)
                {
                    SaveAcknowledgementUnconfirmed = _saves.Values.Any(save =>
                        save.AcknowledgementUnconfirmed && save.State != ReplaySaveState.Validating)
                };
            }
        }
    }

    public Guid BeginSession(RecordingSource source, Guid? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var nextSession = sessionId ?? Guid.NewGuid();
        if (nextSession == Guid.Empty) throw new ArgumentException("A recording session needs a nonempty identity.", nameof(sessionId));
        lock (_gate)
        {
            if (nextSession == _sessionId)
                throw new ArgumentException("A new recording session needs a new identity.", nameof(sessionId));
            _sessionId = nextSession;
            _source = source;
            _profile = null;
            _engineState = RecordingEngineState.Starting;
            _hostHealth = CaptureHostHealth.Starting;
            _bufferedSeconds = _maximumBufferSeconds = 0;
            _estimated = true;
            _trackingLimited = false;
            _sessionEnded = false;
            _saveOutcome = ReplaySaveState.Idle;
            _revision++;
            return nextSession;
        }
    }

    public bool ObserveEngine(Guid sessionId, RecordingEngineState state)
    {
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        lock (_gate)
        {
            if (!IsCurrentSession(sessionId)) return false;
            if (_sessionEnded && state is not (RecordingEngineState.Off or RecordingEngineState.Stopped or RecordingEngineState.Faulted))
                return false;
            if (_engineState != state) _saveOutcome = ReplaySaveState.Idle;
            if (state is RecordingEngineState.Stopped or RecordingEngineState.Faulted ||
                state == RecordingEngineState.Off && _engineState is RecordingEngineState.Ready or RecordingEngineState.Buffering or RecordingEngineState.Stopping)
                _sessionEnded = true;
            _engineState = state;
            if (state is RecordingEngineState.Off or RecordingEngineState.Stopped or RecordingEngineState.Faulted)
            {
                _profile = null;
                _bufferedSeconds = 0;
            }
            _revision++;
            return true;
        }
    }

    public bool ObserveProfile(Guid sessionId, CaptureEncodingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Width <= 0 || profile.Height <= 0 || profile.Fps <= 0 ||
            string.IsNullOrWhiteSpace(profile.EncoderId) || string.IsNullOrWhiteSpace(profile.EncoderName))
            throw new ArgumentException("The observed encoder profile is incomplete.", nameof(profile));
        lock (_gate)
        {
            if (!IsCurrentSession(sessionId) || _sessionEnded ||
                _engineState is RecordingEngineState.Off or RecordingEngineState.Stopped or RecordingEngineState.Faulted) return false;
            _profile = _lastProfile = profile;
            _revision++;
            return true;
        }
    }

    public bool ObserveBuffer(Guid sessionId, double buffered, double maximum,
        bool estimated = true, bool trackingLimited = false)
    {
        if (!double.IsFinite(buffered) || !double.IsFinite(maximum) || buffered < 0 || maximum < 0)
            throw new ArgumentOutOfRangeException(nameof(buffered), "Observed buffer durations must be finite and nonnegative.");
        lock (_gate)
        {
            if (!IsCurrentSession(sessionId) || _sessionEnded) return false;
            _bufferedSeconds = buffered;
            _maximumBufferSeconds = maximum;
            _estimated = estimated;
            _trackingLimited = trackingLimited;
            _revision++;
            return true;
        }
    }

    public bool ObserveHost(Guid sessionId, CaptureHostHealth health)
    {
        if (!Enum.IsDefined(health)) throw new ArgumentOutOfRangeException(nameof(health));
        lock (_gate)
        {
            if (!IsCurrentSession(sessionId)) return false;
            _hostHealth = health;
            _revision++;
            return true;
        }
    }

    public bool RecordRecordingIssue(Guid sessionId, string code, string message, bool faulted = true)
    {
        ValidateIssue(code, message);
        lock (_gate)
        {
            if (sessionId != _sessionId) return false;
            _lastRecordingIssue = new(code, message, _utcNow(), sessionId);
            if (faulted)
            {
                _engineState = RecordingEngineState.Faulted;
                _sessionEnded = true;
                _profile = null;
                _bufferedSeconds = 0;
            }
            _revision++;
            return true;
        }
    }

    public bool BeginSave(Guid sessionId, string saveId)
    {
        ValidateSaveId(saveId);
        lock (_gate)
        {
            if (!IsCurrentSession(sessionId) || _source is null ||
                _engineState is not (RecordingEngineState.Ready or RecordingEngineState.Buffering) || _saves.ContainsKey(saveId)) return false;
            _saves.Add(saveId, new(sessionId, _source, _profile, ReplaySaveState.Requested));
            _saveOutcome = ReplaySaveState.Idle;
            _revision++;
            return true;
        }
    }

    public bool AcknowledgeSave(Guid sessionId, string saveId, bool accepted,
        string? errorCode = null, string? errorMessage = null)
    {
        lock (_gate)
        {
            if (!TryGetSave(sessionId, saveId, out var save)) return false;
            if (!accepted && save.State == ReplaySaveState.Validating) return false;
            if (!accepted)
            {
                CompleteFailure(saveId, save,
                    string.IsNullOrWhiteSpace(errorCode) ? "save_rejected" : errorCode,
                    string.IsNullOrWhiteSpace(errorMessage) ? "The capture host did not accept the replay save." : errorMessage);
            }
            else if (save.State is ReplaySaveState.Requested or ReplaySaveState.Accepted)
            {
                // A late ACK must never move an already validating save backward.
                _saves[saveId] = save with { State = ReplaySaveState.Accepted, AcknowledgementUnconfirmed = false };
            }
            _revision++;
            return true;
        }
    }

    public bool MarkSaveValidating(Guid sessionId, string saveId)
    {
        lock (_gate)
        {
            if (!TryGetSave(sessionId, saveId, out var save)) return false;
            _saves[saveId] = save with { State = ReplaySaveState.Validating, AcknowledgementUnconfirmed = false };
            _revision++;
            return true;
        }
    }

    /// <summary>A command timeout cannot settle a save or supersede a terminal file already being checked.</summary>
    public bool MarkSaveAcknowledgementUnconfirmed(Guid sessionId, string saveId)
    {
        lock (_gate)
        {
            if (!TryGetSave(sessionId, saveId, out var save) || save.State == ReplaySaveState.Validating)
                return false;
            _saves[saveId] = save with { AcknowledgementUnconfirmed = true };
            _revision++;
            return true;
        }
    }

    /// <summary>Call only after the real file has passed validation and durable library registration.</summary>
    public bool RecordValidatedClip(Guid sessionId, string saveId, string clipId, double duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clipId);
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_gate)
        {
            if (!TryGetSave(sessionId, saveId, out var save) || save.State != ReplaySaveState.Validating) return false;
            _saves.Remove(saveId);
            _lastValidatedClip = new(clipId, saveId, sessionId, duration, _utcNow(), save.Source, save.Profile);
            _saveOutcome = ReplaySaveState.Succeeded;
            _revision++;
            return true;
        }
    }

    public bool RecordSaveFailure(Guid sessionId, string saveId, string code, string message)
    {
        ValidateIssue(code, message);
        lock (_gate)
        {
            if (!TryGetSave(sessionId, saveId, out var save)) return false;
            CompleteFailure(saveId, save, code, message);
            _revision++;
            return true;
        }
    }

    /// <summary>Records a local refusal, such as pressing Save while replay is Off.</summary>
    public void RecordSaveIssue(string code, string message)
    {
        ValidateIssue(code, message);
        lock (_gate)
        {
            _lastSaveIssue = new(code, message, _utcNow(), _sessionId);
            _saveOutcome = ReplaySaveState.Failed;
            _revision++;
        }
    }

    public void SetHotkeyHealth(RecordingHotkeyHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentException.ThrowIfNullOrWhiteSpace(health.Action);
        lock (_gate)
        {
            _hotkeys[health.Action] = health;
            _revision++;
        }
    }

    private bool IsCurrentSession(Guid sessionId) => sessionId != Guid.Empty && sessionId == _sessionId;

    private bool TryGetSave(Guid sessionId, string saveId, out PendingSave save)
    {
        save = null!;
        return saveId is not null && _saves.TryGetValue(saveId, out save!) && save.SessionId == sessionId;
    }

    private ReplaySaveState CurrentSaveState() => _saves.Count == 0 ? _saveOutcome
        : _saves.Values.Max(item => item.State);

    private void CompleteFailure(string saveId, PendingSave save, string code, string message)
    {
        _saves.Remove(saveId);
        _lastSaveIssue = new(code, message, _utcNow(), save.SessionId, saveId);
        _saveOutcome = ReplaySaveState.Failed;
    }

    private static void ValidateIssue(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }

    private static void ValidateSaveId(string saveId) => ArgumentException.ThrowIfNullOrWhiteSpace(saveId);
}
