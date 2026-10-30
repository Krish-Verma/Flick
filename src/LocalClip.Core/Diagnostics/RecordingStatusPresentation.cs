using System.Globalization;

namespace LocalClip.Core.Diagnostics;

/// <summary>One text/tone decision for the capture bar and tray. No timers, I/O or capture authority.</summary>
public sealed record RecordingStatusPresentation(string Text, string Detail, string Tone)
{
    public static RecordingStatusPresentation Create(RecordingHealthSnapshot health, string? idleText = null,
        bool recordingTest = false)
    {
        string engine = EngineText(health.EngineState);
        if (health.PendingSaveCount > 0)
        {
            // A stopped/faulted engine does not settle a previously requested save.
            if (health.SaveState == ReplaySaveState.Validating)
                return new("Checking saved video…", $"Verifying the video and adding it to Your clips. {engine}.", "Warning");
            if (health.SaveAcknowledgementUnconfirmed)
                return new("Save still being checked", $"The recorder has not confirmed the result. {engine}.", "Warning");
            return health.SaveState == ReplaySaveState.Requested
                ? new("Save requested…", $"Waiting for the recorder to accept this save. {engine}.", "Warning")
                : new("Saving clip…", $"Finalizing the original video. {engine}.", "Warning");
        }
        if (recordingTest) return new("Testing recording", "The test result appears in Recording health.", "Warning");
        if (health.EngineState == RecordingEngineState.Faulted)
            return new("Recording needs attention", health.LastRecordingIssue?.Message ?? "Review Recording health before restarting replay.", "Danger");
        if (health.SaveState == ReplaySaveState.Failed)
            return new("Couldn't save clip", health.LastSaveIssue?.Message ?? "Review Recording health and try again.", "Danger");
        if (health.HostHealth == CaptureHostHealth.Unresponsive)
            return new("Recording status unavailable", "The recorder is not responding. Review Recording health.", "Warning");
        if (health.SaveState == ReplaySaveState.Succeeded && health.LastValidatedClip is { } clip)
            return new("Clip saved", $"{Number(clip.DurationSeconds)}s saved · {engine.ToLowerInvariant()}", "Success");
        string buffer = BufferText(health);
        return health.EngineState switch
        {
            RecordingEngineState.Ready => new("Replay ready", buffer, "Success"),
            RecordingEngineState.Buffering => new("Building replay", buffer, "Warning"),
            RecordingEngineState.Starting => new("Starting replay…", "Preparing the selected capture source.", "Warning"),
            RecordingEngineState.Stopping => new("Stopping replay…", "Waiting for the recorder to stop.", "Warning"),
            _ => new(string.IsNullOrWhiteSpace(idleText) ? "Replay off" : idleText,
                "Start replay to save recent gameplay.", IdleTone(idleText))
        };
    }

    private static string BufferText(RecordingHealthSnapshot health)
    {
        double retained = double.IsFinite(health.BufferedSeconds) ? Math.Max(0, health.BufferedSeconds) : 0;
        double maximum = double.IsFinite(health.MaximumBufferSeconds) ? Math.Max(0, health.MaximumBufferSeconds) : 0;
        if (maximum > 0) retained = Math.Min(retained, maximum);
        return $"{(health.BufferedDurationEstimated ? "~" : "")}{Number(retained)}s available" +
            (maximum > 0 ? $" · up to {Number(maximum)}s" : "") +
            (health.BufferTrackingLimited ? " · estimated history" : "");
    }

    private static string Number(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
    private static string IdleTone(string? text) => text is "Replay paused" or "Replay paused for memory" or
        "Automatic replay unavailable" or "Start replay to retry" or "Waiting for game" or
        "Waiting to start replay" or "Review Settings" ? "Warning" : "Neutral";
    private static string EngineText(RecordingEngineState state) => state switch
    {
        RecordingEngineState.Ready => "Replay ready",
        RecordingEngineState.Buffering => "Replay building",
        RecordingEngineState.Starting => "Replay starting",
        RecordingEngineState.Stopping => "Replay stopping",
        RecordingEngineState.Faulted => "Recording needs attention",
        _ => "Replay off"
    };
}
