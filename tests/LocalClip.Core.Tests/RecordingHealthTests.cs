using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;

namespace LocalClip.Core.Tests;

public class RecordingHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly RecordingSource Source = new("Window", "Approved game", "window:42");
    private static readonly CaptureEncodingProfile Hardware = new("h264_texture_amf", "AMD AMF", false, 1920, 1080, 60);

    private static (RecordingHealthLedger Ledger, Guid Session) Ready()
    {
        var ledger = new RecordingHealthLedger(() => Now);
        var session = ledger.BeginSession(Source);
        Assert.True(ledger.ObserveProfile(session, Hardware));
        Assert.True(ledger.ObserveEngine(session, RecordingEngineState.Ready));
        Assert.True(ledger.ObserveHost(session, CaptureHostHealth.Responsive));
        return (ledger, session);
    }

    [Fact]
    public void InitialSnapshotIsOffAndDoesNotInventASuccessOrError()
    {
        var snapshot = new RecordingHealthLedger().Snapshot;
        Assert.Equal("Replay Off", snapshot.StatusText);
        Assert.Equal(RecordingEngineState.Off, snapshot.EngineState);
        Assert.Equal(CaptureHostHealth.Stopped, snapshot.HostHealth);
        Assert.Equal(ReplaySaveState.Idle, snapshot.SaveState);
        Assert.Equal(0, snapshot.PendingSaveCount);
        Assert.Null(snapshot.LastValidatedClip);
        Assert.Null(snapshot.LastRecordingIssue);
        Assert.Null(snapshot.LastSaveIssue);
    }

    [Fact]
    public void RequiredUserStatesFollowRealObservationsAndExplicitValidation()
    {
        var ledger = new RecordingHealthLedger(() => Now);
        var session = ledger.BeginSession(Source);
        Assert.Equal("Starting", ledger.Snapshot.StatusText);
        ledger.ObserveEngine(session, RecordingEngineState.Buffering);
        Assert.Equal("Buffering", ledger.Snapshot.StatusText);
        ledger.ObserveEngine(session, RecordingEngineState.Ready);
        Assert.Equal("Replay Ready", ledger.Snapshot.StatusText);
        ledger.BeginSave(session, "save");
        Assert.Equal("Saving Clip", ledger.Snapshot.StatusText);
        ledger.AcknowledgeSave(session, "save", accepted: true);
        ledger.MarkSaveValidating(session, "save");
        ledger.RecordValidatedClip(session, "save", "clip", 14.75);
        Assert.Equal("Save Successful", ledger.Snapshot.StatusText);
        ledger.RecordSaveIssue("off", "Start replay before saving.");
        Assert.Equal("Save Failed", ledger.Snapshot.StatusText);
        ledger.RecordRecordingIssue(session, "host_fault", "Capture stopped producing video.");
        Assert.Equal("Recording Faulted", ledger.Snapshot.StatusText);
        ledger.ObserveEngine(session, RecordingEngineState.Stopped);
        Assert.Equal("Recording Stopped", ledger.Snapshot.StatusText);
        Assert.Equal("host_fault", ledger.Snapshot.LastRecordingIssue!.Code);
    }

    [Fact]
    public void AcceptedAcknowledgementDoesNotMeanSavedOrValidated()
    {
        var (ledger, session) = Ready();
        Assert.True(ledger.BeginSave(session, "save"));
        Assert.True(ledger.AcknowledgeSave(session, "save", accepted: true));
        Assert.Equal(ReplaySaveState.Accepted, ledger.Snapshot.SaveState);
        Assert.Equal(1, ledger.Snapshot.PendingSaveCount);
        Assert.Null(ledger.Snapshot.LastValidatedClip);
        Assert.False(ledger.RecordValidatedClip(session, "save", "clip", 10));
        Assert.Null(ledger.Snapshot.LastValidatedClip);
    }

    [Fact]
    public void LateAcknowledgementsCannotUndoValidationOrResurrectATerminalSave()
    {
        var (ledger, session) = Ready();
        ledger.BeginSave(session, "save");
        Assert.True(ledger.MarkSaveValidating(session, "save"));
        Assert.True(ledger.AcknowledgeSave(session, "save", accepted: true));
        Assert.Equal(ReplaySaveState.Validating, ledger.Snapshot.SaveState);
        Assert.False(ledger.AcknowledgeSave(session, "save", accepted: false));
        Assert.True(ledger.RecordValidatedClip(session, "save", "clip", 10));
        long revision = ledger.Snapshot.Revision;
        Assert.False(ledger.AcknowledgeSave(session, "save", accepted: true));
        Assert.False(ledger.RecordValidatedClip(session, "save", "other-clip", 10));
        Assert.Equal(revision, ledger.Snapshot.Revision);
        Assert.Equal("clip", ledger.Snapshot.LastValidatedClip!.ClipId);
    }

    [Fact]
    public void RejectionAndValidationFailureRemainSeparateFromEngineReadiness()
    {
        var (ledger, session) = Ready();
        ledger.BeginSave(session, "rejected");
        ledger.AcknowledgeSave(session, "rejected", false, "busy", "Another save is in progress.");
        Assert.Equal("busy", ledger.Snapshot.LastSaveIssue!.Code);
        Assert.Equal(RecordingEngineState.Ready, ledger.Snapshot.EngineState);
        ledger.BeginSave(session, "blank-rejection");
        ledger.AcknowledgeSave(session, "blank-rejection", false, "", " ");
        Assert.Equal("save_rejected", ledger.Snapshot.LastSaveIssue!.Code);
        Assert.False(string.IsNullOrWhiteSpace(ledger.Snapshot.LastSaveIssue.Message));
        ledger.BeginSave(session, "invalid-file");
        ledger.MarkSaveValidating(session, "invalid-file");
        ledger.RecordSaveFailure(session, "invalid-file", "invalid_media", "The saved recording could not be validated.");
        Assert.Equal(ReplaySaveState.Failed, ledger.Snapshot.SaveState);
        Assert.Equal(RecordingEngineState.Ready, ledger.Snapshot.EngineState);
        Assert.Null(ledger.Snapshot.LastValidatedClip);
        Assert.Equal(0, ledger.Snapshot.PendingSaveCount);
    }

    [Fact]
    public void NewSessionIgnoresOldEngineProfileHostBufferAndFaultObservations()
    {
        var (ledger, oldSession) = Ready();
        var newSource = new RecordingSource("Monitor", "Display 2", "monitor:1");
        var current = ledger.BeginSession(newSource);
        long revision = ledger.Snapshot.Revision;
        Assert.False(ledger.ObserveEngine(oldSession, RecordingEngineState.Ready));
        Assert.False(ledger.ObserveProfile(oldSession, Hardware));
        Assert.False(ledger.ObserveBuffer(oldSession, 20, 30));
        Assert.False(ledger.ObserveHost(oldSession, CaptureHostHealth.Exited));
        Assert.False(ledger.RecordRecordingIssue(oldSession, "old_fault", "An old helper exited."));
        Assert.Equal(revision, ledger.Snapshot.Revision);
        Assert.Equal(current, ledger.Snapshot.SessionId);
        Assert.Equal(newSource, ledger.Snapshot.Source);
        Assert.Equal(RecordingEngineState.Starting, ledger.Snapshot.EngineState);
    }

    [Fact]
    public void PendingOldSaveSurvivesStopAndRestartWithItsOriginalSourceAndProfile()
    {
        var (ledger, oldSession) = Ready();
        ledger.BeginSave(oldSession, "old-save");
        ledger.AcknowledgeSave(oldSession, "old-save", accepted: true);
        ledger.ObserveEngine(oldSession, RecordingEngineState.Stopped);
        Assert.Equal(1, ledger.Snapshot.PendingSaveCount);
        Assert.Null(ledger.Snapshot.Profile);
        Assert.Equal(Hardware, ledger.Snapshot.LastProfile);
        var newSource = new RecordingSource("Window", "Other game", "window:99");
        var current = ledger.BeginSession(newSource);
        Assert.False(ledger.MarkSaveValidating(current, "old-save"));
        Assert.True(ledger.MarkSaveValidating(oldSession, "old-save"));
        Assert.True(ledger.RecordValidatedClip(oldSession, "old-save", "old-clip", 12));
        var saved = ledger.Snapshot.LastValidatedClip!;
        Assert.Equal(Source, saved.Source);
        Assert.Equal(Hardware, saved.Profile);
        Assert.Equal(oldSession, saved.SessionId);
        Assert.Equal(Now, saved.SavedAt);
        Assert.Equal(newSource, ledger.Snapshot.Source);
        Assert.Equal(RecordingEngineState.Starting, ledger.Snapshot.EngineState);
    }

    [Fact]
    public void StopDoesNotErasePendingSavesAndLateReadyCannotReanimateStoppedSession()
    {
        var (ledger, session) = Ready();
        ledger.BeginSave(session, "pending");
        ledger.ObserveEngine(session, RecordingEngineState.Stopped);
        Assert.False(ledger.ObserveEngine(session, RecordingEngineState.Ready));
        Assert.False(ledger.ObserveBuffer(session, 30, 30));
        Assert.False(ledger.ObserveProfile(session, Hardware));
        Assert.Equal("Saving Clip", ledger.Snapshot.StatusText);
        Assert.Equal(1, ledger.Snapshot.PendingSaveCount);
        Assert.True(ledger.RecordSaveFailure(session, "pending", "host_exit", "The helper exited before completing the save."));
        Assert.Equal(0, ledger.Snapshot.PendingSaveCount);
    }

    [Fact]
    public void MultipleSavesKeepSavingVisibleUntilEveryKnownSaveSettles()
    {
        var (ledger, session) = Ready();
        ledger.BeginSave(session, "one");
        ledger.BeginSave(session, "two");
        Assert.False(ledger.BeginSave(session, "one"));
        Assert.False(ledger.RecordSaveFailure(Guid.NewGuid(), "one", "wrong", "Wrong session."));
        ledger.MarkSaveValidating(session, "one");
        ledger.RecordValidatedClip(session, "one", "first-clip", 5);
        Assert.Equal(1, ledger.Snapshot.PendingSaveCount);
        Assert.Equal("Saving Clip", ledger.Snapshot.StatusText);
        ledger.RecordSaveFailure(session, "two", "storage", "Recording storage is unavailable.");
        Assert.Equal("Save Failed", ledger.Snapshot.StatusText);
        Assert.Equal("first-clip", ledger.Snapshot.LastValidatedClip!.ClipId);
    }

    [Fact]
    public void IssuesAndValidatedSuccessRemainDiscoverableAfterStopAndNewSession()
    {
        var (ledger, session) = Ready();
        ledger.BeginSave(session, "saved");
        ledger.MarkSaveValidating(session, "saved");
        ledger.RecordValidatedClip(session, "saved", "clip", 10);
        ledger.RecordSaveIssue("save_refused", "Another replay save could not be started.");
        ledger.RecordRecordingIssue(session, "runtime", "A recording component failed.");
        ledger.ObserveEngine(session, RecordingEngineState.Stopped);
        ledger.BeginSession(new("Monitor", "Display 1"));
        Assert.Equal("save_refused", ledger.Snapshot.LastSaveIssue!.Code);
        Assert.Equal("runtime", ledger.Snapshot.LastRecordingIssue!.Code);
        Assert.Equal("clip", ledger.Snapshot.LastValidatedClip!.ClipId);
        Assert.Equal("Starting", ledger.Snapshot.StatusText);
    }

    [Fact]
    public void OffStateSaveIssueDoesNotPretendToStartCapture()
    {
        var ledger = new RecordingHealthLedger(() => Now);
        ledger.RecordSaveIssue("replay_off", "Start replay before saving.");
        Assert.Equal(RecordingEngineState.Off, ledger.Snapshot.EngineState);
        Assert.Equal(0, ledger.Snapshot.PendingSaveCount);
        Assert.Equal("Save Failed", ledger.Snapshot.StatusText);
        Assert.Equal(Guid.Empty, ledger.Snapshot.LastSaveIssue!.SessionId);
        Assert.False(ledger.BeginSave(Guid.Empty, "not-started"));
    }

    [Fact]
    public void HotkeyHealthReportsActualRegistrationAndSnapshotsCannotBeMutated()
    {
        var ledger = new RecordingHealthLedger();
        ledger.SetHotkeyHealth(new("Save", "Ctrl+F8", null, false, 1409, "Shortcut already registered."));
        var earlier = ledger.Snapshot;
        ledger.SetHotkeyHealth(new("Save", "Ctrl+F8", "Ctrl+F8", true));
        Assert.False(Assert.Single(earlier.Hotkeys).IsRegistered);
        Assert.Equal(1409, Assert.Single(earlier.Hotkeys).Win32Error);
        Assert.True(Assert.Single(ledger.Snapshot.Hotkeys).IsRegistered);
        Assert.Throws<NotSupportedException>(() => ((IList<RecordingHotkeyHealth>)earlier.Hotkeys).Clear());
    }

    [Fact]
    public void BufferEstimateAndTrackingLimitAreHonestAndRejectInvalidNumbers()
    {
        var (ledger, session) = Ready();
        Assert.True(ledger.ObserveBuffer(session, 4.25, 30, estimated: true, trackingLimited: true));
        Assert.Equal(4.25, ledger.Snapshot.BufferedSeconds);
        Assert.Equal(30, ledger.Snapshot.MaximumBufferSeconds);
        Assert.True(ledger.Snapshot.BufferedDurationEstimated);
        Assert.True(ledger.Snapshot.BufferTrackingLimited);
        long revision = ledger.Snapshot.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.ObserveBuffer(session, double.NaN, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.ObserveBuffer(session, 10, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.ObserveBuffer(session, -1, 30));
        Assert.Equal(revision, ledger.Snapshot.Revision);
    }

    [Fact]
    public void InvalidValidatedResultCannotSettleAPendingSave()
    {
        var (ledger, session) = Ready();
        ledger.BeginSave(session, "save");
        ledger.MarkSaveValidating(session, "save");
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.RecordValidatedClip(session, "save", "clip", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.RecordValidatedClip(session, "save", "clip", 0));
        Assert.Throws<ArgumentException>(() => ledger.RecordValidatedClip(session, "save", "", 10));
        Assert.Equal(1, ledger.Snapshot.PendingSaveCount);
        Assert.Null(ledger.Snapshot.LastValidatedClip);
    }

    [Fact]
    public void UnresponsiveHostDoesNotInventAnEngineFaultOrKeepAdvertisingReady()
    {
        var (ledger, session) = Ready();
        ledger.ObserveHost(session, CaptureHostHealth.Unresponsive);
        ledger.RecordRecordingIssue(session, "status_timeout", "Recording status could not be confirmed.", faulted: false);
        Assert.Equal(RecordingEngineState.Ready, ledger.Snapshot.EngineState);
        Assert.Equal("Recording Status Unavailable", ledger.Snapshot.StatusText);
        Assert.NotNull(ledger.Snapshot.Profile);
        ledger.BeginSave(session, "already-accepted");
        Assert.Equal("Saving Clip", ledger.Snapshot.StatusText);
        ledger.RecordSaveFailure(session, "already-accepted", "save_failed", "The saved recording could not be validated.");
        Assert.Equal("Recording Status Unavailable", ledger.Snapshot.StatusText);
        ledger.ObserveHost(session, CaptureHostHealth.Responsive);
        Assert.Equal(RecordingEngineState.Ready, ledger.Snapshot.EngineState);
        Assert.Equal("Save Failed", ledger.Snapshot.StatusText);
        Assert.Equal("status_timeout", ledger.Snapshot.LastRecordingIssue!.Code);
    }

    [Fact]
    public void ConcurrentSaveCallbacksCannotLoseOrDoubleSettleKnownOperations()
    {
        var (ledger, session) = Ready();
        Parallel.For(0, 100, index =>
        {
            string id = "save-" + index;
            Assert.True(ledger.BeginSave(session, id));
            Assert.True(ledger.AcknowledgeSave(session, id, accepted: true));
            Assert.True(ledger.MarkSaveValidating(session, id));
            Assert.True(ledger.RecordValidatedClip(session, id, "clip-" + index, 5));
            Assert.False(ledger.RecordValidatedClip(session, id, "duplicate", 5));
        });
        Assert.Equal(0, ledger.Snapshot.PendingSaveCount);
        Assert.NotNull(ledger.Snapshot.LastValidatedClip);
    }
}
