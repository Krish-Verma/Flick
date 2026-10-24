using LocalClip.Core.Ipc;
using LocalClip.Core.Models;

namespace LocalClip.Core.Diagnostics;

/// <summary>
/// Runs one consented test through an owned helper and the real registration pipeline.
/// Backends must honor cancellation and settle their work; this controller never abandons a timed-out task.
/// </summary>
public sealed class RecordingDiagnosticRunner
{
    private readonly Func<RecordingDiagnosticPlan, IRecordingDiagnosticLease?> _factory;
    private readonly Func<RecordingDiagnosticPlan, RecordingDiagnosticReplay, CancellationToken, Task<ClipMetadata>> _register;
    private readonly TimeProvider _clock;
    private readonly RecordingDiagnosticOptions _options;
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public RecordingDiagnosticRunner(Func<RecordingDiagnosticPlan, IRecordingDiagnosticLease?> factory,
        Func<RecordingDiagnosticPlan, RecordingDiagnosticReplay, CancellationToken, Task<ClipMetadata>> validateAndRegister,
        TimeProvider? clock = null, RecordingDiagnosticOptions? options = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _register = validateAndRegister ?? throw new ArgumentNullException(nameof(validateAndRegister));
        _clock = clock ?? TimeProvider.System;
        _options = options ?? new RecordingDiagnosticOptions();
        _options.Validate();
    }

    public async Task<RecordingDiagnosticResult> RunAsync(RecordingDiagnosticPlan plan, Guid confirmedPlanId,
        IProgress<RecordingDiagnosticProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (confirmedPlanId != plan.PlanId)
            return Refused(RecordingDiagnosticFailure.ConsentRequired, "Confirm the displayed source and audio choices before starting the test.");
        if (cancellationToken.IsCancellationRequested)
            return new(RecordingDiagnosticOutcome.Cancelled, RecordingDiagnosticFailure.Cancelled,
                "The recording test was cancelled before starting.", null, null, new(), true);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return Refused(RecordingDiagnosticFailure.Busy, "A recording test is already running.");

        long started = _clock.GetTimestamp();
        var timings = new RecordingDiagnosticTimings();
        RecordingDiagnosticStage stage = RecordingDiagnosticStage.Starting;
        RecordingDiagnosticOutcome outcome = RecordingDiagnosticOutcome.Failed;
        RecordingDiagnosticFailure failure = RecordingDiagnosticFailure.None;
        string message = "The recording test did not complete.";
        CaptureEncodingProfile? profile = null;
        ClipMetadata? clip = null;
        IRecordingDiagnosticLease? lease = null;
        string? runDirectory = null;
        bool exited = true;
        var cleanupGate = new object();
        Task<(bool Exited, TimeSpan Elapsed)>? cleanupTask = null;
        using var workDeadline = new CancellationTokenSource(_options.WorkTimeout, _clock);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, workDeadline.Token);

        Task<(bool Exited, TimeSpan Elapsed)>? StartCleanup()
        {
            lock (cleanupGate)
            {
                if (lease == null) return null;
                var ownedLease = lease;
                // A validator may be settling an atomic index write when the
                // user cancels. Stop recording independently, but retain and
                // await this task alongside the validator before returning.
                return cleanupTask ??= Task.Run(async () =>
                {
                    long cleanupStarted = _clock.GetTimestamp();
                    using var cleanup = new CancellationTokenSource(_options.CleanupTimeout, _clock);
                    bool confirmed;
                    try { confirmed = await ownedLease.StopAndConfirmExitAsync(cleanup.Token).ConfigureAwait(false); }
                    catch { confirmed = false; }
                    return (confirmed, _clock.GetElapsedTime(cleanupStarted));
                });
            }
        }

        // Only schedule work from a token callback: an adapter may need to
        // close a process handle, which must never block the cancelling UI.
        using var stopOnCancellation = work.Token.Register(() => { _ = StartCleanup(); });

        void Report(RecordingDiagnosticStage next)
        {
            stage = next;
            try { progress?.Report(new(next, _clock.GetElapsedTime(started))); }
            catch { /* A local progress consumer cannot interrupt capture cleanup. */ }
        }

        async Task<T> Phase<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout,
            Action<TimeSpan> measured)
        {
            using var deadline = new CancellationTokenSource(timeout, _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(work.Token, deadline.Token);
            using var stopOnPhaseCancellation = linked.Token.Register(() => { _ = StartCleanup(); });
            long phaseStarted = _clock.GetTimestamp();
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                T value = await operation(linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                return value;
            }
            finally { measured(_clock.GetElapsedTime(phaseStarted)); }
        }

        try
        {
            work.Token.ThrowIfCancellationRequested();
            var acquiredLease = _factory(plan);
            lock (cleanupGate) lease = acquiredLease;
            if (lease == null)
            {
                outcome = RecordingDiagnosticOutcome.Refused;
                failure = RecordingDiagnosticFailure.Busy;
                message = "Finish the active recording or other work before running a recording test.";
            }
            else
            {
                exited = false;
                // Cancellation may have arrived while the factory was
                // acquiring the lease, before it could be observed above.
                if (work.IsCancellationRequested) _ = StartCleanup();
                work.Token.ThrowIfCancellationRequested();
                runDirectory = CreateRunDirectory(plan.OriginalsRoot);
                Report(RecordingDiagnosticStage.Starting);
                profile = await Phase(token => lease.StartAsync(plan, runDirectory, token), _options.StartupTimeout,
                    elapsed => timings = timings with { Startup = elapsed }).ConfigureAwait(false);
                if (profile == null || string.IsNullOrWhiteSpace(profile.EncoderId) ||
                    string.IsNullOrWhiteSpace(profile.EncoderName) || profile.Width <= 0 || profile.Height <= 0 || profile.Fps <= 0)
                    throw new InvalidDataException("The test helper did not report its actual recording profile.");

                Report(RecordingDiagnosticStage.WaitingForReady);
                // Short replay configurations must remain testable without
                // altering preferences or waiting for an unreachable full buffer.
                double requiredBufferSeconds = Math.Min(_options.MinimumBufferedDuration.TotalSeconds,
                    plan.ReplayDurationSeconds * 0.8);
                await Phase(async token =>
                {
                    while (true)
                    {
                        var status = await lease.GetStatusAsync(token).ConfigureAwait(false);
                        if (status.State is "Faulted" or "Off" or "Stopping" || !status.TargetValid ||
                            !string.IsNullOrWhiteSpace(status.FaultReason))
                            throw new InvalidOperationException("The selected source or recording engine became unavailable during the test.");
                        if (status.State == "Armed" && status.IsArmed &&
                            double.IsFinite(status.BufferedSec) && status.BufferedSec >= requiredBufferSeconds)
                            return true;
                        await Task.Delay(_options.PollInterval, _clock, token).ConfigureAwait(false);
                    }
                }, _options.ReadinessTimeout, elapsed => timings = timings with { Readiness = elapsed }).ConfigureAwait(false);

                string saveId = "diagnostic_" + Guid.NewGuid().ToString("N");
                Report(RecordingDiagnosticStage.Saving);
                var replay = await Phase(token => lease.SaveAndWaitAsync(saveId, token), _options.SaveTimeout,
                    elapsed => timings = timings with { Save = elapsed }).ConfigureAwait(false);
                try { ValidateReplay(runDirectory, saveId, replay); }
                catch { failure = RecordingDiagnosticFailure.InvalidReplay; throw; }

                Report(RecordingDiagnosticStage.Validating);
                await Phase(async token =>
                {
                    var registered = await _register(plan, replay, token).ConfigureAwait(false);
                    ValidateRegisteredClip(replay, registered, profile);
                    // A cancellation just after the atomic index commit must not
                    // hide the saved clip or delete the original from the result.
                    clip = registered;
                    return true;
                }, _options.ValidationTimeout, elapsed => timings = timings with { Validation = elapsed }).ConfigureAwait(false);
                outcome = RecordingDiagnosticOutcome.Succeeded;
                message = "The test clip was validated and saved in your library.";
            }
        }
        catch (OperationCanceledException)
        {
            outcome = cancellationToken.IsCancellationRequested ? RecordingDiagnosticOutcome.Cancelled : RecordingDiagnosticOutcome.Failed;
            failure = cancellationToken.IsCancellationRequested ? RecordingDiagnosticFailure.Cancelled : RecordingDiagnosticFailure.TimedOut;
            message = clip != null ? "The test ended after its clip was saved. The original was kept in your library."
                : cancellationToken.IsCancellationRequested ? "The recording test was cancelled. Any produced original was kept."
                : "The recording test reached its time limit. Any produced original was kept.";
        }
        catch (Exception error)
        {
            if (failure == RecordingDiagnosticFailure.None)
                failure = stage switch
                {
                    RecordingDiagnosticStage.WaitingForReady => RecordingDiagnosticFailure.NotReady,
                    RecordingDiagnosticStage.Saving => RecordingDiagnosticFailure.SaveFailed,
                    RecordingDiagnosticStage.Validating => RecordingDiagnosticFailure.ValidationFailed,
                    _ => RecordingDiagnosticFailure.StartupFailed
                };
            message = "Recording test failed: " + error.Message;
        }
        finally
        {
            if (lease != null)
            {
                Report(RecordingDiagnosticStage.Stopping);
                TimeSpan cleanupElapsed = TimeSpan.Zero;
                try
                {
                    var stopped = await StartCleanup()!.ConfigureAwait(false);
                    exited = stopped.Exited;
                    cleanupElapsed = stopped.Elapsed;
                }
                catch { exited = false; }
                long disposalStarted = _clock.GetTimestamp();
                try { await lease.DisposeAsync().ConfigureAwait(false); }
                catch { exited = false; }
                timings = timings with { Cleanup = cleanupElapsed + _clock.GetElapsedTime(disposalStarted) };
                if (!exited)
                {
                    outcome = RecordingDiagnosticOutcome.CleanupIncomplete;
                    failure = RecordingDiagnosticFailure.CleanupFailed;
                    message = "The test helper's exit could not be confirmed. Any saved original was kept; close Flick before testing again.";
                }
                else if (outcome == RecordingDiagnosticOutcome.Succeeded && cancellationToken.IsCancellationRequested)
                {
                    outcome = RecordingDiagnosticOutcome.Cancelled;
                    failure = RecordingDiagnosticFailure.Cancelled;
                    message = "The test was cancelled after its clip was saved. The original was kept in your library.";
                }
            }
            timings = timings with { Total = _clock.GetElapsedTime(started) };
            // An unconfirmed helper may still own capture resources. Refuse a
            // second test on this controller until the app has been closed.
            if (exited) Volatile.Write(ref _running, 0);
        }
        Report(RecordingDiagnosticStage.Completed);
        return new(outcome, failure, message, profile, clip, timings, exited, runDirectory);
    }

    private static RecordingDiagnosticResult Refused(RecordingDiagnosticFailure failure, string message) =>
        new(RecordingDiagnosticOutcome.Refused, failure, message, null, null, new(), true);

    private static string CreateRunDirectory(string originalsRoot)
    {
        string parent = Path.Combine(originalsRoot, "Diagnostics");
        Directory.CreateDirectory(parent);
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Choose an originals folder with a regular diagnostics directory.");
        string path = Path.Combine(parent, "test" + Guid.NewGuid().ToString("N"));
        if (Path.Exists(path)) throw new IOException("The test output directory is already occupied.");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void ValidateReplay(string runDirectory, string saveId, RecordingDiagnosticReplay replay)
    {
        if (replay == null || replay.SaveId != saveId || string.IsNullOrWhiteSpace(replay.FilePath) || replay.TrackNames == null)
            throw new InvalidDataException("The saved replay did not match this test request.");
        string expected = Path.Combine(runDirectory, "Flick_" + saveId + ".mkv");
        if (!SamePath(expected, replay.FilePath) ||
            (File.GetAttributes(runDirectory) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(replay.FilePath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            new FileInfo(replay.FilePath).Length == 0)
            throw new InvalidDataException("The test helper did not return a new regular replay file inside its owned test directory.");
    }

    private static void ValidateRegisteredClip(RecordingDiagnosticReplay replay, ClipMetadata clip, CaptureEncodingProfile profile)
    {
        if (clip == null || string.IsNullOrWhiteSpace(clip.Id) || !SamePath(replay.FilePath, clip.OriginalFilePath) ||
            clip.IsImported || !double.IsFinite(clip.DurationSeconds) || clip.DurationSeconds <= 0 ||
            clip.Width != profile.Width || clip.Height != profile.Height || clip.Fps != profile.Fps)
            throw new InvalidDataException("The saved test clip did not pass media and library registration validation.");
    }

    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
