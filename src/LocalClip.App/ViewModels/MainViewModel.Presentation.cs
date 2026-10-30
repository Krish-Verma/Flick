using LocalClip.Core.Diagnostics;

namespace LocalClip.App.ViewModels;

public partial class MainViewModel
{
    private RecordingStatusPresentation CapturePresentation
    {
        get
        {
            var health = _health.Snapshot;
            // Render fixtures and the unstarted UI have no recording session.
            // This changes presentation only; it never fabricates ledger readiness.
            if (health.SessionId == Guid.Empty)
                health = health with { EngineState = CaptureStatus switch
                {
                    "Armed" => RecordingEngineState.Ready,
                    "Buffering" => RecordingEngineState.Buffering,
                    "Starting" => RecordingEngineState.Starting,
                    "Stopping" => RecordingEngineState.Stopping,
                    "Faulted" => RecordingEngineState.Faulted,
                    _ => RecordingEngineState.Off
                } };
            return RecordingStatusPresentation.Create(health, ReplayWaitingText, IsRecordingDiagnosticRunning);
        }
    }

    public string CaptureStateText => CapturePresentation.Text;
    public string CaptureStateDetail => CapturePresentation.Detail;
    public string CaptureStateTone => CapturePresentation.Tone;
    public string CaptureSourceCaption => IsAutomaticGameCapture
        ? AutomaticGameSourceText : SelectedTarget?.DisplayName ?? "Choose a capture source";

    private void PublishCapturePresentation()
    {
        foreach (string name in new[] { nameof(CaptureStateText), nameof(CaptureStateDetail),
                     nameof(CaptureStateTone), nameof(CaptureSourceCaption) })
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
