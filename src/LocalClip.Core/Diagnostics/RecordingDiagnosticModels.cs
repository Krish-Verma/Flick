using LocalClip.Core.Ipc;
using LocalClip.Core.Models;

namespace LocalClip.Core.Diagnostics;

public sealed record RecordingDiagnosticTarget(string Kind, string Name,
    int MonitorIndex = 0, string MonitorId = "", ulong WindowHwnd = 0,
    uint WindowPid = 0, string WindowTitle = "", string WindowExecutable = "",
    string WindowExecutablePath = "", ulong WindowProcessCreationFileTime = 0);

public sealed record RecordingDiagnosticReplay(string SaveId, string FilePath, IReadOnlyList<string> TrackNames);

/// <summary>
/// Owns one private helper. Methods must settle their owned work before returning on cancellation.
/// Cleanup and disposal must share one bounded cleanup operation and never start a helper.
/// </summary>
public interface IRecordingDiagnosticLease : IAsyncDisposable
{
    Task<CaptureEncodingProfile> StartAsync(RecordingDiagnosticPlan plan, string outputDirectory,
        CancellationToken cancellationToken);
    Task<CaptureHostStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<RecordingDiagnosticReplay> SaveAndWaitAsync(string saveId, CancellationToken cancellationToken);
    Task<bool> StopAndConfirmExitAsync(CancellationToken cleanupToken);
}

public enum RecordingDiagnosticStage { Starting, WaitingForReady, Saving, Validating, Stopping, Completed }
public enum RecordingDiagnosticOutcome { Succeeded, Refused, Cancelled, Failed, CleanupIncomplete }
public enum RecordingDiagnosticFailure
{
    None, ConsentRequired, Busy, InvalidPlan, StartupFailed, NotReady, SaveFailed,
    InvalidReplay, ValidationFailed, TimedOut, Cancelled, CleanupFailed
}

public sealed record RecordingDiagnosticProgress(RecordingDiagnosticStage Stage, TimeSpan Elapsed);

public sealed record RecordingDiagnosticTimings(TimeSpan? Startup = null, TimeSpan? Readiness = null,
    TimeSpan? Save = null, TimeSpan? Validation = null, TimeSpan? Cleanup = null, TimeSpan? Total = null);

/// <summary>Local UI result: never serialize source paths, media metadata or messages into a diagnostic journal.</summary>
public sealed record RecordingDiagnosticResult(RecordingDiagnosticOutcome Outcome,
    RecordingDiagnosticFailure Failure, string LocalMessage, CaptureEncodingProfile? ActualProfile,
    ClipMetadata? ValidatedClip, RecordingDiagnosticTimings Timings, bool HelperExitConfirmed,
    string? RunDirectory = null)
{
    public bool Succeeded => Outcome == RecordingDiagnosticOutcome.Succeeded &&
        ValidatedClip != null && HelperExitConfirmed;
}

public sealed record RecordingDiagnosticOptions
{
    public TimeSpan WorkTimeout { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ReadinessTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan SaveTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ValidationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(35);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Desired readiness span, capped at 80% of the frozen replay length for short-buffer tests.</summary>
    public TimeSpan MinimumBufferedDuration { get; init; } = TimeSpan.FromSeconds(3);

    internal void Validate()
    {
        foreach (var duration in new[] { WorkTimeout, StartupTimeout, ReadinessTimeout, SaveTimeout,
                     ValidationTimeout, CleanupTimeout, PollInterval, MinimumBufferedDuration })
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10))
                throw new ArgumentOutOfRangeException(nameof(RecordingDiagnosticOptions), "Test deadlines must be positive and bounded.");
    }
}
