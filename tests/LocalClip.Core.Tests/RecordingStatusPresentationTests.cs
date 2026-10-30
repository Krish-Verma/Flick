using LocalClip.Core.Diagnostics;

namespace LocalClip.Core.Tests;

public class RecordingStatusPresentationTests
{
    private static (RecordingHealthLedger Ledger, Guid Session) Ready()
    {
        var ledger = new RecordingHealthLedger();
        var session = ledger.BeginSession(new RecordingSource("Window", "Approved game"));
        ledger.ObserveEngine(session, RecordingEngineState.Ready);
        ledger.ObserveHost(session, CaptureHostHealth.Responsive);
        return (ledger, session);
    }

    [Fact]
    public void MissingAckRemainsUnconfirmedThroughEngineStopUntilRealFileValidationCompletes()
    {
        var (ledger, session) = Ready();
        Assert.True(ledger.BeginSave(session, "save"));
        Assert.Equal("Save requested…", RecordingStatusPresentation.Create(ledger.Snapshot).Text);
        Assert.True(ledger.MarkSaveAcknowledgementUnconfirmed(session, "save"));
        ledger.ObserveEngine(session, RecordingEngineState.Stopped);
        var waiting = RecordingStatusPresentation.Create(ledger.Snapshot, "Replay off");
        Assert.Equal("Save still being checked", waiting.Text);
        Assert.Equal("Warning", waiting.Tone);
        Assert.Contains("Replay off", waiting.Detail);
        Assert.Null(ledger.Snapshot.LastSaveIssue);
        Assert.True(ledger.MarkSaveValidating(session, "save"));
        Assert.False(ledger.Snapshot.SaveAcknowledgementUnconfirmed);
        Assert.Equal("Checking saved video…", RecordingStatusPresentation.Create(ledger.Snapshot).Text);
        Assert.False(ledger.MarkSaveAcknowledgementUnconfirmed(session, "save"));
        Assert.False(ledger.AcknowledgeSave(session, "save", false));
        Assert.True(ledger.RecordValidatedClip(session, "save", "clip", 12));
        var saved = RecordingStatusPresentation.Create(ledger.Snapshot, "Replay off");
        Assert.Equal("Clip saved", saved.Text);
        Assert.Contains("12s saved", saved.Detail);
        Assert.Equal("Success", saved.Tone);
        Assert.False(ledger.MarkSaveAcknowledgementUnconfirmed(session, "save"));
    }

    [Fact]
    public void LateAcceptedAckClearsOnlyItsOwnUncertaintyAndFailureCannotLookReady()
    {
        var (ledger, session) = Ready();
        ledger.BeginSave(session, "save");
        Assert.False(ledger.MarkSaveAcknowledgementUnconfirmed(Guid.NewGuid(), "save"));
        Assert.False(ledger.MarkSaveAcknowledgementUnconfirmed(session, "unknown"));
        ledger.MarkSaveAcknowledgementUnconfirmed(session, "save");
        Assert.True(ledger.AcknowledgeSave(session, "save", true));
        Assert.False(ledger.Snapshot.SaveAcknowledgementUnconfirmed);
        Assert.Equal("Saving clip…", RecordingStatusPresentation.Create(ledger.Snapshot).Text);
        ledger.RecordSaveFailure(session, "save", "full", "Free disk space and try again.");
        var failed = RecordingStatusPresentation.Create(ledger.Snapshot);
        Assert.Equal("Couldn't save clip", failed.Text);
        Assert.Equal("Danger", failed.Tone);
        Assert.Contains("Free disk space", failed.Detail);
        Assert.Equal(RecordingEngineState.Ready, ledger.Snapshot.EngineState);
    }

    [Fact]
    public void BufferShowsObservedHistoryRatherThanRequestedReplayDuration()
    {
        var (ledger, session) = Ready();
        ledger.ObserveBuffer(session, 18, 30, estimated: true, trackingLimited: true);
        var ready = RecordingStatusPresentation.Create(ledger.Snapshot);
        Assert.Equal("Replay ready", ready.Text);
        Assert.Equal("Success", ready.Tone);
        Assert.Contains("~18s available", ready.Detail);
        Assert.Contains("up to 30s", ready.Detail);
        ledger.ObserveEngine(session, RecordingEngineState.Buffering);
        Assert.Equal("Building replay", RecordingStatusPresentation.Create(ledger.Snapshot).Text);
        Assert.Equal("Warning", RecordingStatusPresentation.Create(ledger.Snapshot).Tone);
    }

    [Fact]
    public void ValidationSurvivesNewSessionAndDoesNotBecomeFaultOrOffMessage()
    {
        var (ledger, session) = Ready();
        ledger.BeginSave(session, "old-save");
        ledger.MarkSaveValidating(session, "old-save");
        var next = ledger.BeginSession(new RecordingSource("Window", "Other approved game"));
        ledger.RecordRecordingIssue(next, "target", "Source closed.");
        var pending = RecordingStatusPresentation.Create(ledger.Snapshot);
        Assert.Equal("Checking saved video…", pending.Text);
        Assert.Equal("Warning", pending.Tone);
        Assert.Contains("needs attention", pending.Detail);
        Assert.True(ledger.RecordValidatedClip(session, "old-save", "clip", 8));
        Assert.Equal("Recording needs attention", RecordingStatusPresentation.Create(ledger.Snapshot).Text);
    }

    [Fact]
    public void IdleAndTestingDoNotInventCaptureReadiness()
    {
        var ledger = new RecordingHealthLedger();
        Assert.Equal("Replay off", RecordingStatusPresentation.Create(ledger.Snapshot).Text);
        Assert.Equal("Waiting for game", RecordingStatusPresentation.Create(ledger.Snapshot, "Waiting for game").Text);
        Assert.Equal("Warning", RecordingStatusPresentation.Create(ledger.Snapshot, "Waiting for game").Tone);
        Assert.Equal("Testing recording", RecordingStatusPresentation.Create(ledger.Snapshot, recordingTest: true).Text);
        foreach (string waiting in new[] { "Replay paused", "Replay paused for memory", "Review Settings", "Waiting for game", "Choose a source", "Start replay to retry", "Automatic replay unavailable" })
            Assert.Equal(waiting, RecordingStatusPresentation.Create(ledger.Snapshot, waiting).Text);
        Assert.Equal("Warning", RecordingStatusPresentation.Create(ledger.Snapshot, "Replay paused for memory").Tone);
        Assert.Null(ledger.Snapshot.LastValidatedClip);
    }
}
