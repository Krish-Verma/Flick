using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;

namespace LocalClip.Core.Tests;

public sealed class DiagnosticPolicyTests
{
    [Fact]
    public void ValidatedOldSaveUsesItsOriginalSourceAndProfileDuringANewRecordingSession()
    {
        var ledger = new RecordingHealthLedger();
        var oldSession = ledger.BeginSession(new("Monitor", "Private old display", "private:old"));
        ledger.ObserveProfile(oldSession, new("obs_x264", "Private encoder label", true, 1280, 720, 30));
        ledger.ObserveEngine(oldSession, RecordingEngineState.Ready);
        Assert.True(ledger.BeginSave(oldSession, "private-save-id"));
        Assert.True(ledger.AcknowledgeSave(oldSession, "private-save-id", accepted: true));
        ledger.ObserveEngine(oldSession, RecordingEngineState.Stopped);

        var current = ledger.BeginSession(new("Window", "Private new game", "private:new"));
        ledger.ObserveProfile(current, new("h264_texture_amf", "Private current encoder", false, 2560, 1440, 60));
        ledger.ObserveEngine(current, RecordingEngineState.Ready);
        Assert.True(ledger.MarkSaveValidating(oldSession, "private-save-id"));
        Assert.True(ledger.RecordValidatedClip(oldSession, "private-save-id", "private-clip-id", 12.75));

        var saved = DiagnosticObservation.FromHealth(DiagnosticEventKind.SaveValidated, ledger.Snapshot);
        Assert.Equal(DiagnosticSourceKind.Monitor, saved.SourceKind);
        Assert.Equal(DiagnosticEncoder.CpuX264, saved.Encoder);
        Assert.Equal(1280, saved.Width);
        Assert.Equal(720, saved.Height);
        Assert.Equal(30, saved.Fps);
        Assert.True(saved.SoftwareFallback);
        Assert.Equal(12750, saved.DurationMilliseconds);

        var currentEvent = DiagnosticObservation.FromHealth(DiagnosticEventKind.EngineChanged, ledger.Snapshot);
        Assert.Equal(DiagnosticSourceKind.Window, currentEvent.SourceKind);
        Assert.Equal(DiagnosticEncoder.AmdTexture, currentEvent.Encoder);
        Assert.Equal(2560, currentEvent.Width);
        Assert.Null(currentEvent.DurationMilliseconds);
    }

    [Fact]
    public void StoppedEngineDoesNotReportTheLastProfileAsAnActiveEncoder()
    {
        var ledger = new RecordingHealthLedger();
        var session = ledger.BeginSession(new("Window", "Private game"));
        ledger.ObserveProfile(session, new("obs_nvenc_h264_tex", "NVIDIA NVENC", false, 1920, 1080, 60));
        ledger.ObserveEngine(session, RecordingEngineState.Stopped);
        Assert.NotNull(ledger.Snapshot.LastProfile);

        var item = DiagnosticObservation.FromHealth(DiagnosticEventKind.EngineChanged, ledger.Snapshot);

        Assert.Equal(RecordingEngineState.Stopped, item.EngineState);
        Assert.Equal(DiagnosticEncoder.Unknown, item.Encoder);
        Assert.Null(item.Width);
        Assert.Null(item.Height);
        Assert.Null(item.Fps);
        Assert.Null(item.SoftwareFallback);
    }

    [Fact]
    public void ObservationSchemaCannotAcquireFreeTextIdentifiersOrCollections()
    {
        // This guards the privacy boundary when new diagnostic fields are added.
        foreach (var property in typeof(DiagnosticObservation).GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            Assert.True(type.IsEnum || type == typeof(bool) || type == typeof(int) || type == typeof(long),
                $"Diagnostic field {property.Name} must use a closed enum or bounded numeric value.");
        }
    }

    [Fact]
    public void ReplayControlDiagnosticsRecordOnlyClosedActionAndOriginCategories()
    {
        var observed = new DiagnosticObservation(DiagnosticEventKind.ReplayControl,
            ControlOrigin: DiagnosticControlOrigin.GlobalHotkey,
            ControlAction: DiagnosticControlAction.StopDeferred);
        Assert.True(DiagnosticPolicy.IsValid(observed));
        Assert.False(DiagnosticPolicy.IsValid(observed with
            { ControlOrigin = (DiagnosticControlOrigin)1000 }));
        Assert.False(DiagnosticPolicy.IsValid(observed with
            { ControlAction = (DiagnosticControlAction)1000 }));
        Assert.DoesNotContain(typeof(DiagnosticObservation).GetProperties(),
            property => property.Name.Contains("KeyCode", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ffmpeg_nvenc", DiagnosticEncoder.NvidiaFfmpeg)]
    [InlineData("jim_nvenc", DiagnosticEncoder.NvidiaLegacy)]
    [InlineData("PRIVATE_DEVICE_ID", DiagnosticEncoder.Unknown)]
    public void LegacyEncoderIdentifiersStayWithinTheClosedAllowlist(string identifier, DiagnosticEncoder expected) =>
        Assert.Equal(expected, DiagnosticPolicy.KnownEncoder(identifier));
}
