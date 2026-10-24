using System.Diagnostics;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;

namespace Flick.RecordingHarness;

internal sealed record CaseEvidence(string Id, string Outcome, double ElapsedMilliseconds,
    RecordingDiagnosticResult? Recording, CaptureEncodingProfile? ObservedProfile,
    int StopCalls, int DisposeCalls, int IndexedClips, bool ExitBeforeValidationReleased,
    bool OwnedMediaWorkersExitConfirmed, SampledHostMetrics HostMetrics, MediaEvidence? Media, string? Error);
internal sealed record SampledHostMetrics(int StatusSamples, long PeakPrivateBytes, long PeakWorkingSetBytes);

internal sealed class QualificationSuite(HarnessOptions options, OwnedOutput output, PatternWindow window)
{
    public const int ExpectedCases = 7;
    private enum CaseKind { Normal, CancelReadiness, MinimizeActive, CancelValidation }
    private readonly List<CaseEvidence> _cases = [];
    private readonly Dictionary<string, string> _originalHashes = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        (string Id, CaseKind Kind)[] cases =
        [
            ("01-save-decode-pattern-silence", CaseKind.Normal),
            ("02-repeat-fresh-helper", CaseKind.Normal),
            ("03-cancel-at-readiness", CaseKind.CancelReadiness),
            ("04-recover-after-cancel", CaseKind.Normal),
            ("05-minimized-active-source-no-false-success", CaseKind.MinimizeActive),
            ("06-cancel-held-validation-retain-index", CaseKind.CancelValidation),
            ("07-recover-after-failures", CaseKind.Normal)
        ];
        bool cleanupConfirmed = true;
        foreach (var test in cases)
        {
            if (cancellationToken.IsCancellationRequested || !cleanupConfirmed)
            {
                var notRun = new CaseEvidence(test.Id, "NotRun", 0, null, null, 0, 0, 0, false, true, new(0, 0, 0), null,
                    cleanupConfirmed ? "Suite cancelled or reached its deadline." : "Previous helper exit was not confirmed.");
                _cases.Add(notRun);
                output.WriteNew(test.Id + ".json", notRun);
                continue;
            }
            Console.WriteLine("Running " + test.Id);
            CaseEvidence result = await RunCaseAsync(test.Id, test.Kind, cancellationToken);
            _cases.Add(result);
            output.WriteNew(test.Id + ".json", result);
            cleanupConfirmed = (result.Recording?.HelperExitConfirmed ?? result.StopCalls == 0) && result.OwnedMediaWorkersExitConfirmed;
            Console.WriteLine(test.Id + ": " + result.Outcome);
        }

        string? immutabilityError = null;
        try
        {
            foreach (var original in _originalHashes)
                if (await OwnedOutput.HashAsync(original.Key) != original.Value)
                    throw new InvalidDataException("An earlier original changed during a later test case.");
        }
        catch (Exception error) { immutabilityError = error.Message; }
        bool accepted = _cases.Count == ExpectedCases && _cases.All(result => result.Outcome == "Passed") &&
            immutabilityError == null && cleanupConfirmed;
        output.WriteNew("qualification-result.json", new
        {
            SchemaVersion = 1, output.RunId, ExpectedCases, ExecutedCases = _cases.Count(c => c.Outcome != "NotRun"),
            PassedCases = _cases.Count(c => c.Outcome == "Passed"), FailedCases = _cases.Count(c => c.Outcome == "Failed"),
            NotRunCases = _cases.Count(c => c.Outcome == "NotRun"), Accepted = accepted,
            HelperExitConfirmed = cleanupConfirmed, OriginalImmutabilityPassed = immutabilityError == null,
            OriginalImmutabilityError = immutabilityError,
            PreservedOriginals = _originalHashes.Select(pair => new { File = Path.GetRelativePath(output.Root, pair.Key), Sha256 = pair.Value }),
            Cases = _cases,
            UnqualifiedGates = new[]
            {
                "Same-buffer rapid saves", "Physical hotkey delivery/conflict", "Forced helper crash/restart",
                "System or microphone audio endpoints", "Gameplay FPS and long-duration performance",
                "Other GPUs/drivers/HDR/multi-DPI", "Discord delivery", "Signing and Smart App Control"
            }
        });
        return accepted;
    }

    private async Task<CaseEvidence> RunCaseAsync(string id, CaseKind kind, CancellationToken overall)
    {
        var timer = Stopwatch.StartNew();
        RecordingDiagnosticResult? recording = null;
        MediaEvidence? media = null;
        ObservingLease? lease = null;
        ClipStorage? storage = null;
        bool exitBeforeRelease = false;
        bool mediaWorkersExited = true;
        string? error = null;
        string caseRoot = output.NewDirectory(id);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        using var work = CancellationTokenSource.CreateLinkedTokenSource(overall, deadline.Token);
        try
        {
            await window.RestoreAsync();
            CaptureWindowIdentity identity = await window.ShowReadyAsync(work.Token);
            var settings = CreateSettings(caseRoot, options.Encoder);
            Directory.CreateDirectory(settings.OriginalsFolder);
            Directory.CreateDirectory(settings.ExportsFolder);
            Directory.CreateDirectory(settings.TempFolder);
            var prober = new MediaProber(options.Ffprobe, options.Ffmpeg);
            string metadata = Path.Combine(caseRoot, "Metadata");
            storage = new ClipStorage(settings, prober, metadataDirectory: metadata);
            if (storage.GetAllClips().Count != 0 || storage.LastLoadWarning.Length != 0)
                throw new InvalidOperationException("New isolated library was not empty and healthy.");
            var target = new RecordingDiagnosticTarget("Window", PatternWindow.FixtureTitle,
                WindowHwnd: identity.Hwnd, WindowPid: identity.ProcessId, WindowTitle: identity.Title,
                WindowExecutable: identity.Executable);
            var plan = RecordingDiagnosticPlan.Create(settings, target, enableSystemAudio: false, enableMicrophone: false);
            var runner = new RecordingDiagnosticRunner(_ => lease = new ObservingLease(
                    new NativeRecordingDiagnosticLease(options.Host), options.Encoder,
                    kind == CaseKind.MinimizeActive ? window.MinimizeAsync : null),
                async (_, replay, token) =>
                {
                    var registered = await storage.RegisterNewOriginalAsync(replay.FilePath, PatternWindow.FixtureTitle,
                        replay.TrackNames, token).ConfigureAwait(false);
                    if (kind == CaseKind.CancelValidation)
                    {
                        // Model a completed atomic registration whose caller is still
                        // settling. Cancellation must stop capture before it returns.
                        await work.CancelAsync().ConfigureAwait(false);
                        exitBeforeRelease = await lease!.ExitConfirmed.Task.WaitAsync(TimeSpan.FromSeconds(40)).ConfigureAwait(false);
                        if (!exitBeforeRelease) throw new InvalidOperationException("Owned helper did not exit while validation was held.");
                    }
                    return registered;
                });
            var progress = new DirectProgress(p =>
            {
                if (kind == CaseKind.CancelReadiness && p.Stage == RecordingDiagnosticStage.WaitingForReady)
                    _ = work.CancelAsync();
            });
            recording = await runner.RunAsync(plan, plan.PlanId, progress, work.Token);
            if (!recording.HelperExitConfirmed || lease == null || lease.StopCalls != 1 || lease.DisposeCalls != 1)
                throw new InvalidOperationException("Owned helper cleanup was not confirmed exactly once.");

            switch (kind)
            {
                case CaseKind.Normal:
                    if (!recording.Succeeded || storage.GetAllClips().Count != 1)
                        throw new InvalidDataException("Expected one validated, indexed successful replay: " + recording.LocalMessage);
                    break;
                case CaseKind.CancelReadiness:
                    if (recording.Outcome != RecordingDiagnosticOutcome.Cancelled || recording.ValidatedClip != null || storage.GetAllClips().Count != 0)
                        throw new InvalidDataException("Readiness cancellation reported success or created an index entry.");
                    break;
                case CaseKind.MinimizeActive:
                    if (recording.Outcome != RecordingDiagnosticOutcome.Failed || recording.ValidatedClip != null || storage.GetAllClips().Count != 0 ||
                        recording.Failure is not (RecordingDiagnosticFailure.NotReady or RecordingDiagnosticFailure.TimedOut))
                        throw new InvalidDataException("Minimized fixture produced a false success or an unexpected lifecycle outcome.");
                    break;
                case CaseKind.CancelValidation:
                    if (recording.Outcome != RecordingDiagnosticOutcome.Cancelled || recording.ValidatedClip == null ||
                        storage.GetAllClips().Count != 1 || !exitBeforeRelease)
                        throw new InvalidDataException("Cancellation lost a committed clip or failed to stop capture before releasing validation.");
                    break;
            }
            if (recording.ValidatedClip is { } clip)
            {
                // A new storage instance proves that the atomic index is durable.
                var reloaded = new ClipStorage(settings, prober, metadataDirectory: metadata);
                if (reloaded.GetAllClips().Count != 1 || reloaded.GetClipById(clip.Id)?.OriginalFilePath != clip.OriginalFilePath)
                    throw new InvalidDataException("Validated replay did not survive isolated index reload.");
                using var verificationDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                using var verification = CancellationTokenSource.CreateLinkedTokenSource(overall, verificationDeadline.Token);
                media = await MediaVerification.VerifyAsync(options, clip, verification.Token);
                _originalHashes.Add(clip.OriginalFilePath, media.OriginalSha256);
            }
        }
        catch (Exception failure)
        {
            error = failure.Message;
            if (failure is OwnedWorkerCleanupException) mediaWorkersExited = false;
        }
        finally
        {
            if (lease != null && lease.DisposeCalls == 0)
            {
                // Covers a failure in the harness itself before the production runner's finally.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                try { await lease.StopAndConfirmExitAsync(cleanup.Token); await lease.DisposeAsync(); }
                catch (Exception failure) { error ??= "Harness cleanup failed: " + failure.Message; }
            }
            try { await window.RestoreAsync(); }
            catch (Exception failure) { error ??= "Fixture restore failed: " + failure.Message; }
            // Preserve and fingerprint failed-case originals too. Never delete any case output.
            try
            {
                foreach (string original in EnumerateOriginals(caseRoot))
                    if (!_originalHashes.ContainsKey(original)) _originalHashes.Add(original, await OwnedOutput.HashAsync(original));
            }
            catch (Exception failure) { error ??= "Original evidence could not be preserved: " + failure.Message; }
        }
        return new(id, error == null ? "Passed" : "Failed", timer.Elapsed.TotalMilliseconds, recording,
            lease?.Profile, lease?.StopCalls ?? 0, lease?.DisposeCalls ?? 0, storage?.GetAllClips().Count ?? 0,
            exitBeforeRelease, mediaWorkersExited, lease?.Metrics ?? new(0, 0, 0), media, error);
    }

    internal static AppSettings CreateSettings(string root, string encoder) => new()
    {
        TargetType = "Window", VideoWidth = 640, VideoHeight = 360, VideoFps = 30,
        VideoBitrateKbps = 2500, VideoEncoder = encoder, AllowSoftwareFallback = encoder == "obs_x264",
        ReplayDurationSeconds = 5, BufferMemoryLimitMb = 64, EnableMicrophone = false,
        StorageRoot = root, OriginalsFolder = Path.Combine(root, "Originals"),
        ExportsFolder = Path.Combine(root, "Exports"), TempFolder = Path.Combine(root, "Temp"),
        EnableDiscordWebhook = false, StartWithWindows = false, PlayClipSavedSound = false, ShowClipSavedOverlay = false
    };

    private static IEnumerable<string> EnumerateOriginals(string directory)
    {
        OwnedOutput.RefuseReparseComponents(directory);
        foreach (string file in Directory.EnumerateFiles(directory, "*.mkv"))
        { OwnedOutput.RefuseReparseComponents(file); yield return file; }
        foreach (string child in Directory.EnumerateDirectories(directory))
            foreach (string file in EnumerateOriginals(child)) yield return file;
    }

    private sealed class DirectProgress(Action<RecordingDiagnosticProgress> report) : IProgress<RecordingDiagnosticProgress>
    {
        public void Report(RecordingDiagnosticProgress value) => report(value);
    }

    private sealed class ObservingLease(IRecordingDiagnosticLease inner, string expectedEncoder,
        Func<Task>? afterStart) : IRecordingDiagnosticLease
    {
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public CaptureEncodingProfile? Profile { get; private set; }
        public SampledHostMetrics Metrics { get; private set; } = new(0, 0, 0);
        public TaskCompletionSource<bool> ExitConfirmed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CaptureEncodingProfile> StartAsync(RecordingDiagnosticPlan plan, string directory, CancellationToken token)
        {
            Profile = await inner.StartAsync(plan, directory, token).ConfigureAwait(false);
            if (Profile.EncoderId != expectedEncoder || Profile.IsSoftwareFallback != (expectedEncoder == "obs_x264") ||
                Profile.Width != 640 || Profile.Height != 360 || Profile.Fps != 30)
                throw new InvalidDataException("Actual encoder/profile differs from the explicitly requested qualification profile.");
            if (afterStart != null) await afterStart().ConfigureAwait(false);
            return Profile;
        }
        public async Task<CaptureHostStatus> GetStatusAsync(CancellationToken token)
        {
            var status = await inner.GetStatusAsync(token).ConfigureAwait(false);
            Metrics = new(Metrics.StatusSamples + 1, Math.Max(Metrics.PeakPrivateBytes, status.PrivateBytes),
                Math.Max(Metrics.PeakWorkingSetBytes, status.WorkingSet));
            return status;
        }
        public Task<RecordingDiagnosticReplay> SaveAndWaitAsync(string saveId, CancellationToken token) => inner.SaveAndWaitAsync(saveId, token);
        public async Task<bool> StopAndConfirmExitAsync(CancellationToken token)
        {
            StopCalls++;
            try
            {
                bool exited = await inner.StopAndConfirmExitAsync(token).ConfigureAwait(false);
                ExitConfirmed.TrySetResult(exited);
                return exited;
            }
            catch { ExitConfirmed.TrySetResult(false); throw; }
        }
        public async ValueTask DisposeAsync()
        { DisposeCalls++; await inner.DisposeAsync().ConfigureAwait(false); }
    }
}
