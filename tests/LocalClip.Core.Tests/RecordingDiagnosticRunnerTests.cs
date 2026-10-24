using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;

namespace LocalClip.Core.Tests;

/// <summary>Controller fixtures exercise ownership and validation gates; their bytes are not playable media.</summary>
public sealed class RecordingDiagnosticRunnerTests
{
    [Fact]
    public async Task ConsentMustMatchTheExactFrozenPlanBeforeFactoryOrFilesystemWork()
    {
        using var fixture = new Fixture();
        var runner = fixture.Runner();

        var result = await runner.RunAsync(fixture.Plan, Guid.NewGuid());

        Assert.Equal(RecordingDiagnosticOutcome.Refused, result.Outcome);
        Assert.Equal(RecordingDiagnosticFailure.ConsentRequired, result.Failure);
        Assert.Equal(0, fixture.FactoryCalls);
        Assert.False(Directory.Exists(fixture.Settings.OriginalsFolder));
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public async Task PreCancelledTestDoesNotAcquireALeaseOrCreateOutput()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId,
            cancellationToken: cancellation.Token);

        Assert.Equal(RecordingDiagnosticOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, fixture.FactoryCalls);
        Assert.False(Directory.Exists(fixture.Settings.OriginalsFolder));
    }

    [Fact]
    public async Task FactoryRefusalCannotStartOrCreateATestDirectory()
    {
        using var fixture = new Fixture { RefuseLease = true };
        var runner = fixture.Runner();

        var result = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.Equal(RecordingDiagnosticFailure.Busy, result.Failure);
        Assert.Equal(RecordingDiagnosticOutcome.Refused, result.Outcome);
        Assert.Equal(0, fixture.Lease.Starts);
        Assert.Equal(0, fixture.Lease.Stops);
        Assert.False(Directory.Exists(fixture.Settings.OriginalsFolder));
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public async Task CancellationDuringLeaseAcquisitionCleansItWithoutStartingCapture()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var runner = new RecordingDiagnosticRunner(_ =>
        {
            cancellation.Cancel();
            return fixture.Lease;
        }, (_, replay, _) => Task.FromResult(fixture.ValidClip(replay)), options: QuickOptions());

        var result = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId, cancellationToken: cancellation.Token);

        Assert.Equal(RecordingDiagnosticOutcome.Cancelled, result.Outcome);
        Assert.True(result.HelperExitConfirmed);
        Assert.Equal(0, fixture.Lease.Starts);
        Assert.Equal(1, fixture.Lease.Stops);
        Assert.Equal(1, fixture.Lease.Disposals);
        Assert.False(Directory.Exists(fixture.Settings.OriginalsFolder));
    }

    [Fact]
    public async Task SuccessRequiresCorrelatedFileRegistrationAndConfirmedOwnedExit()
    {
        using var fixture = new Fixture();
        var stages = new List<RecordingDiagnosticStage>();
        var runner = fixture.Runner();

        var result = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId,
            new ImmediateProgress(value => stages.Add(value.Stage)));

        Assert.True(result.Succeeded);
        Assert.Equal(RecordingDiagnosticFailure.None, result.Failure);
        Assert.Equal(fixture.Lease.Profile, result.ActualProfile);
        Assert.Same(fixture.RegisteredClip, result.ValidatedClip);
        Assert.Equal(1, fixture.Registrations);
        Assert.Equal(1, fixture.Lease.Starts);
        Assert.Equal(1, fixture.Lease.Saves);
        Assert.Equal(1, fixture.Lease.Stops);
        Assert.Equal(1, fixture.Lease.Disposals);
        Assert.True(result.HelperExitConfirmed);
        Assert.True(File.Exists(result.ValidatedClip!.OriginalFilePath));
        Assert.Equal(Path.Combine(result.RunDirectory!, "Flick_" + fixture.Lease.LastSaveId + ".mkv"),
            result.ValidatedClip.OriginalFilePath);
        Assert.Equal(new[] { RecordingDiagnosticStage.Starting, RecordingDiagnosticStage.WaitingForReady,
            RecordingDiagnosticStage.Saving, RecordingDiagnosticStage.Validating,
            RecordingDiagnosticStage.Stopping, RecordingDiagnosticStage.Completed }, stages);
        Assert.NotNull(result.Timings.Startup);
        Assert.NotNull(result.Timings.Readiness);
        Assert.NotNull(result.Timings.Save);
        Assert.NotNull(result.Timings.Validation);
        Assert.NotNull(result.Timings.Cleanup);
        Assert.True(result.Timings.Total >= result.Timings.Validation);
        Assert.False(runner.IsRunning);
    }

    [Theory]
    [InlineData(1, 0.8)]
    [InlineData(2, 1.6)]
    public async Task ShortConfiguredReplayUsesReachableReadinessWithoutChangingPreferences(int seconds, double buffered)
    {
        using var fixture = new Fixture(replaySeconds: seconds);
        fixture.Lease.Status.BufferedSec = buffered;

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.True(result.Succeeded);
        Assert.Equal(seconds, fixture.Settings.ReplayDurationSeconds);
        Assert.Equal(seconds, fixture.Plan.ReplayDurationSeconds);
    }

    [Fact]
    public void PlanFreezesCaptureValuesAndAudioConsentWithoutCopyingAccountOrUiPreferences()
    {
        using var fixture = new Fixture();
        fixture.Settings.EnableMicrophone = true;
        fixture.Settings.EnableDiscordWebhook = true;
        fixture.Settings.MaskedWebhookUrl = "private-account-canary";
        fixture.Settings.WindowTitle = "unselected-private-window";
        var plan = RecordingDiagnosticPlan.Create(fixture.Settings, Fixture.Target);
        fixture.Settings.VideoWidth = 99;
        fixture.Settings.VideoEncoder = "mutated";
        fixture.Settings.SystemAudioDeviceId = "mutated-device";
        fixture.Settings.OriginalsFolder = "mutated-folder";

        var settings = CaptureSettings(plan, fixture.Root);
        var second = CaptureSettings(plan, fixture.Root);

        Assert.False(plan.EnableSystemAudio);
        Assert.False(plan.EnableMicrophone);
        Assert.Contains("Computer audio: Off", plan.ConsentSummary);
        Assert.Contains("Microphone: Off", plan.ConsentSummary);
        Assert.Equal(1920, settings.VideoWidth);
        Assert.Equal("auto", settings.VideoEncoder);
        Assert.Equal("default", settings.SystemAudioDeviceId);
        Assert.Equal(Fixture.Target.WindowTitle, settings.WindowTitle);
        Assert.Equal(fixture.Root, settings.OriginalsFolder);
        Assert.Equal("SystemFallback", settings.AudioMode);
        Assert.False(settings.EnableMicrophone);
        Assert.False(settings.EnableDiscordWebhook);
        Assert.Empty(settings.MaskedWebhookUrl);
        Assert.NotSame(settings, second);
        settings.VideoWidth = 7;
        Assert.Equal(1920, second.VideoWidth);
        Assert.False(Directory.Exists(plan.OriginalsRoot));
    }

    [Theory]
    [InlineData(0UL, 17U, "Fixture", "fixture.exe")]
    [InlineData(42UL, 0U, "Fixture", "fixture.exe")]
    [InlineData(42UL, 17U, "", "fixture.exe")]
    [InlineData(42UL, 17U, "Fixture", "")]
    public void WindowPlanRequiresExactCurrentIdentity(ulong handle, uint pid, string title, string executable)
    {
        using var fixture = new Fixture();

        Assert.Throws<ArgumentException>(() => RecordingDiagnosticPlan.Create(fixture.Settings,
            new("Window", "Fixture", WindowHwnd: handle, WindowPid: pid,
                WindowTitle: title, WindowExecutable: executable)));

        Assert.False(Directory.Exists(fixture.Settings.OriginalsFolder));
    }

    [Fact]
    public void MonitorPlanRequiresDeviceIdentityAndRootMustBeAbsolute()
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => RecordingDiagnosticPlan.Create(fixture.Settings,
            new("Monitor", "Display", MonitorIndex: 0)));
        fixture.Settings.OriginalsFolder = "relative-originals";
        Assert.Throws<ArgumentException>(() => RecordingDiagnosticPlan.Create(fixture.Settings, Fixture.Target));
    }

    [Fact]
    public async Task StartupTimeoutSettlesOwnedOperationAndUsesIndependentCleanupBudget()
    {
        using var fixture = new Fixture();
        bool settled = false;
        fixture.Lease.OnStart = async token =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { settled = true; }
            return fixture.Lease.Profile;
        };
        fixture.Lease.OnStop = token =>
        {
            Assert.False(token.IsCancellationRequested);
            return Task.FromResult(true);
        };

        var result = await fixture.Runner(QuickOptions() with { StartupTimeout = TimeSpan.FromMilliseconds(30) })
            .RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.True(settled);
        Assert.Equal(RecordingDiagnosticFailure.TimedOut, result.Failure);
        Assert.False(result.Succeeded);
        Assert.True(result.HelperExitConfirmed);
        Assert.Equal(0, fixture.Lease.Saves);
        Assert.Equal(1, fixture.Lease.Disposals);
    }

    [Theory]
    [InlineData("Buffering", false, 5)]
    [InlineData("Armed", false, 5)]
    [InlineData("Armed", true, 0)]
    [InlineData("Armed", true, double.NaN)]
    public async Task ReadinessRequiresArmedAndObservedFiniteRetainedDuration(string state, bool armed, double buffered)
    {
        using var fixture = new Fixture();
        fixture.Lease.Status.State = state;
        fixture.Lease.Status.IsArmed = armed;
        fixture.Lease.Status.BufferedSec = buffered;

        var result = await fixture.Runner(QuickOptions() with { ReadinessTimeout = TimeSpan.FromMilliseconds(25) })
            .RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.Equal(RecordingDiagnosticFailure.TimedOut, result.Failure);
        Assert.Equal(0, fixture.Lease.Saves);
        Assert.True(result.HelperExitConfirmed);
    }

    [Theory]
    [InlineData("Faulted", true, "Encoder stopped")]
    [InlineData("Off", true, "")]
    [InlineData("Armed", false, "")]
    [InlineData("Armed", true, "Source closed")]
    public async Task UnavailableTargetOrEngineFailsBeforeSave(string state, bool valid, string fault)
    {
        using var fixture = new Fixture();
        fixture.Lease.Status.State = state;
        fixture.Lease.Status.TargetValid = valid;
        fixture.Lease.Status.FaultReason = fault;

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.Equal(RecordingDiagnosticFailure.NotReady, result.Failure);
        Assert.Equal(0, fixture.Lease.Saves);
        Assert.Equal(0, fixture.Registrations);
        Assert.True(result.HelperExitConfirmed);
    }

    [Fact]
    public async Task WorkDeadlineIncludesReadinessAndDoesNotAbandonTheStatusRequest()
    {
        using var fixture = new Fixture();
        bool settled = false;
        fixture.Lease.OnStatus = async token =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { settled = true; }
            return fixture.Lease.Status;
        };

        var result = await fixture.Runner(QuickOptions() with { WorkTimeout = TimeSpan.FromMilliseconds(30) })
            .RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.True(settled);
        Assert.Equal(RecordingDiagnosticFailure.TimedOut, result.Failure);
        Assert.Equal(1, fixture.Lease.Stops);
    }

    [Fact]
    public async Task CancellingSavePreservesProducedOriginalAndSettlesBeforeReturning()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        bool settled = false;
        fixture.Lease.OnSave = async (id, token) =>
        {
            var replay = fixture.Lease.WriteReplay(id);
            cancellation.Cancel();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { settled = true; }
            return replay;
        };
        fixture.Lease.OnStop = token =>
        {
            Assert.False(token.IsCancellationRequested);
            return Task.FromResult(true);
        };

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId,
            cancellationToken: cancellation.Token);

        Assert.Equal(RecordingDiagnosticOutcome.Cancelled, result.Outcome);
        Assert.True(settled);
        Assert.Equal(0, fixture.Registrations);
        Assert.True(File.Exists(fixture.Lease.LastFile));
        Assert.True(result.HelperExitConfirmed);
    }

    [Fact]
    public async Task CancellationAfterAtomicRegistrationRetainsTheRegisteredClip()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.OnRegister = (replay, _) =>
        {
            var clip = fixture.ValidClip(replay);
            cancellation.Cancel();
            return Task.FromResult(clip);
        };

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId,
            cancellationToken: cancellation.Token);

        Assert.Equal(RecordingDiagnosticOutcome.Cancelled, result.Outcome);
        Assert.NotNull(result.ValidatedClip);
        Assert.True(File.Exists(result.ValidatedClip.OriginalFilePath));
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CancellationStopsAndConfirmsHelperExitWhileAtomicValidationIsStillHeld()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var validationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishValidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var helperExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.OnRegister = async (replay, _) =>
        {
            validationEntered.SetResult();
            // Model a short atomic index operation that must settle even when
            // cancellation arrives. Recording must stop independently of it.
            await finishValidation.Task;
            return fixture.ValidClip(replay);
        };
        fixture.Lease.OnStop = token =>
        {
            Assert.False(token.IsCancellationRequested);
            helperExited.SetResult();
            return Task.FromResult(true);
        };
        var runner = fixture.Runner();
        var running = runner.RunAsync(fixture.Plan, fixture.Plan.PlanId, cancellationToken: cancellation.Token);
        await validationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        try
        {
            await helperExited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(running.IsCompleted);
            Assert.False(finishValidation.Task.IsCompleted);
            Assert.True(File.Exists(fixture.Lease.LastFile));
            Assert.Equal(1, fixture.Lease.Stops);
        }
        finally { finishValidation.TrySetResult(); }
        var result = await running.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(RecordingDiagnosticOutcome.Cancelled, result.Outcome);
        Assert.True(result.HelperExitConfirmed);
        Assert.NotNull(result.ValidatedClip);
        Assert.True(File.Exists(result.ValidatedClip.OriginalFilePath));
        Assert.Equal(1, fixture.Lease.Stops);
        Assert.Equal(1, fixture.Lease.Disposals);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public async Task ValidationDeadlineAlsoStopsHelperBeforeAnUninterruptibleCommitSettles()
    {
        using var fixture = new Fixture();
        var validationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishValidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var helperExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.OnRegister = async (replay, _) =>
        {
            validationEntered.SetResult();
            await finishValidation.Task;
            return fixture.ValidClip(replay);
        };
        fixture.Lease.OnStop = _ =>
        {
            helperExited.SetResult();
            return Task.FromResult(true);
        };
        var runner = fixture.Runner(QuickOptions() with { ValidationTimeout = TimeSpan.FromMilliseconds(50) });
        var running = runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);
        await validationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await helperExited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(running.IsCompleted);
            Assert.False(finishValidation.Task.IsCompleted);
        }
        finally { finishValidation.TrySetResult(); }
        var result = await running.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(RecordingDiagnosticFailure.TimedOut, result.Failure);
        Assert.True(result.HelperExitConfirmed);
        Assert.NotNull(result.ValidatedClip);
        Assert.True(File.Exists(result.ValidatedClip.OriginalFilePath));
        Assert.Equal(1, fixture.Lease.Stops);
        Assert.Equal(1, fixture.Lease.Disposals);
    }

    [Fact]
    public async Task ValidationTimeoutSettlesValidatorAndKeepsOriginal()
    {
        using var fixture = new Fixture();
        bool settled = false;
        fixture.OnRegister = async (replay, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { settled = true; }
            return fixture.ValidClip(replay);
        };

        var result = await fixture.Runner(QuickOptions() with { ValidationTimeout = TimeSpan.FromMilliseconds(30) })
            .RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.True(settled);
        Assert.Equal(RecordingDiagnosticFailure.TimedOut, result.Failure);
        Assert.Null(result.ValidatedClip);
        Assert.True(File.Exists(fixture.Lease.LastFile));
    }

    [Fact]
    public async Task SaveTimeoutPreservesItsPartialOriginalAndSettlesBeforeReturning()
    {
        using var fixture = new Fixture();
        bool settled = false;
        fixture.Lease.OnSave = async (id, token) =>
        {
            var replay = fixture.Lease.WriteReplay(id);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { settled = true; }
            return replay;
        };

        var result = await fixture.Runner(QuickOptions() with { SaveTimeout = TimeSpan.FromMilliseconds(30) })
            .RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.True(settled);
        Assert.Equal(RecordingDiagnosticFailure.TimedOut, result.Failure);
        Assert.True(File.Exists(fixture.Lease.LastFile));
        Assert.Equal(0, fixture.Registrations);
        Assert.True(result.HelperExitConfirmed);
    }

    [Fact]
    public async Task CleanupTimeoutCannotTurnAValidatedClipIntoAReportedPass()
    {
        using var fixture = new Fixture();
        bool settled = false;
        fixture.Lease.OnStop = async token =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { settled = true; }
            return true;
        };
        var runner = fixture.Runner(QuickOptions() with { CleanupTimeout = TimeSpan.FromMilliseconds(30) });

        var result = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.True(settled);
        Assert.Equal(RecordingDiagnosticOutcome.CleanupIncomplete, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.ValidatedClip);
        Assert.True(File.Exists(result.ValidatedClip.OriginalFilePath));
        Assert.True(runner.IsRunning);
        Assert.Equal(1, fixture.Lease.Disposals);
    }

    [Fact]
    public async Task OccupiedDiagnosticsDirectoryFailsWithoutStartingOrOverwritingAnything()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Settings.OriginalsFolder);
        string collision = Path.Combine(fixture.Settings.OriginalsFolder, "Diagnostics");
        const string sentinel = "preexisting-user-file-must-not-be-replaced";
        await File.WriteAllTextAsync(collision, sentinel);

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.Equal(RecordingDiagnosticFailure.StartupFailed, result.Failure);
        Assert.Equal(0, fixture.Lease.Starts);
        Assert.Equal(sentinel, await File.ReadAllTextAsync(collision));
        Assert.True(result.HelperExitConfirmed);
    }

    [Fact]
    public async Task ReentryIsRefusedUntilTheFirstOwnedRunHasCompletelyExited()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Lease.OnStart = async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return fixture.Lease.Profile;
        };
        var runner = fixture.Runner();
        var first = runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.Equal(RecordingDiagnosticFailure.Busy, rejected.Failure);
        Assert.Equal(1, fixture.FactoryCalls);
        Assert.True(runner.IsRunning);
        release.SetResult();
        Assert.True((await first).Succeeded);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public async Task UnconfirmedExitRetainsValidatedClipAndPermanentlyRefusesReuse()
    {
        using var fixture = new Fixture();
        fixture.Lease.OnStop = _ => Task.FromResult(false);
        var runner = fixture.Runner();

        var result = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);
        var next = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.Equal(RecordingDiagnosticOutcome.CleanupIncomplete, result.Outcome);
        Assert.False(result.HelperExitConfirmed);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.ValidatedClip);
        Assert.True(File.Exists(result.ValidatedClip.OriginalFilePath));
        Assert.True(runner.IsRunning);
        Assert.Equal(RecordingDiagnosticFailure.Busy, next.Failure);
        Assert.Equal(1, fixture.FactoryCalls);
        Assert.Equal(1, fixture.Lease.Disposals);
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("outside")]
    [InlineData("other-name")]
    [InlineData("missing")]
    [InlineData("empty")]
    public async Task WrongOrUnownedReplayNeverReachesRegistration(string kind)
    {
        using var fixture = new Fixture();
        string? produced = null;
        fixture.Lease.OnSave = (id, _) =>
        {
            var replay = fixture.Lease.WriteReplay(id);
            produced = replay.FilePath;
            switch (kind)
            {
                case "wrong-id": replay = replay with { SaveId = "other-save" }; break;
                case "outside":
                    produced = Path.Combine(fixture.Root, "foreign.mkv");
                    File.WriteAllText(produced, "foreign-file-must-survive");
                    replay = replay with { FilePath = produced }; break;
                case "other-name":
                    produced = Path.Combine(fixture.Lease.OutputDirectory!, "other.mkv");
                    File.WriteAllText(produced, "unrelated-file-must-survive");
                    replay = replay with { FilePath = produced }; break;
                case "missing": File.Delete(produced); break;
                case "empty": File.WriteAllBytes(produced, []); break;
            }
            return Task.FromResult(replay);
        };

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.Equal(RecordingDiagnosticFailure.InvalidReplay, result.Failure);
        Assert.Equal(0, fixture.Registrations);
        Assert.True(result.HelperExitConfirmed);
        if (kind != "missing") Assert.True(File.Exists(produced));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("imported")]
    [InlineData("duration")]
    [InlineData("profile")]
    public async Task InvalidRegisteredMediaCannotBeCalledSuccessful(string kind)
    {
        using var fixture = new Fixture();
        fixture.OnRegister = (replay, _) =>
        {
            var clip = fixture.ValidClip(replay);
            switch (kind)
            {
                case "path": clip.OriginalFilePath = Path.Combine(fixture.Root, "other.mkv"); break;
                case "imported": clip.SourceKind = "Imported"; break;
                case "duration": clip.DurationSeconds = double.NaN; break;
                case "profile": clip.Width = 1920; break;
            }
            return Task.FromResult(clip);
        };

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.Equal(RecordingDiagnosticFailure.ValidationFailed, result.Failure);
        Assert.False(result.Succeeded);
        Assert.True(File.Exists(fixture.Lease.LastFile));
    }

    [Fact]
    public async Task RepeatedTestsUseUniqueDirectoriesAndNeverReplacePriorOriginals()
    {
        using var fixture = new Fixture();
        var runner = fixture.Runner();

        var first = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);
        var second = await runner.RunAsync(fixture.Plan, fixture.Plan.PlanId);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.RunDirectory, second.RunDirectory);
        Assert.NotEqual(first.ValidatedClip!.OriginalFilePath, second.ValidatedClip!.OriginalFilePath);
        Assert.True(File.Exists(first.ValidatedClip.OriginalFilePath));
        Assert.True(File.Exists(second.ValidatedClip.OriginalFilePath));
    }

    [Fact]
    public async Task ThrowingProgressConsumerCannotPreventOwnedCleanup()
    {
        using var fixture = new Fixture();

        var result = await fixture.Runner().RunAsync(fixture.Plan, fixture.Plan.PlanId,
            new ImmediateProgress(_ => throw new InvalidOperationException("UI fixture closed")));

        Assert.True(result.Succeeded);
        Assert.Equal(1, fixture.Lease.Stops);
        Assert.Equal(1, fixture.Lease.Disposals);
    }

    [Fact]
    public async Task RegistrationHonorsPreCancellationBeforeProbingOrWritingAnIndex()
    {
        using var fixture = new Fixture();
        string metadata = Path.Combine(fixture.Root, "metadata");
        var storage = new ClipStorage(fixture.Settings, metadataDirectory: metadata);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.RegisterNewOriginalAsync(
            Path.Combine(fixture.Root, "not-created.mkv"), cancellationToken: cancellation.Token));

        Assert.Empty(storage.GetAllClips());
        Assert.False(File.Exists(Path.Combine(metadata, "clips_index.json")));
    }

    private static AppSettings CaptureSettings(RecordingDiagnosticPlan plan, string output) => plan.CreateCaptureSettings(output);

    private static RecordingDiagnosticOptions QuickOptions() => new()
    {
        WorkTimeout = TimeSpan.FromSeconds(2), StartupTimeout = TimeSpan.FromSeconds(1),
        ReadinessTimeout = TimeSpan.FromSeconds(1), SaveTimeout = TimeSpan.FromSeconds(1),
        ValidationTimeout = TimeSpan.FromSeconds(1), CleanupTimeout = TimeSpan.FromSeconds(1),
        PollInterval = TimeSpan.FromMilliseconds(1)
    };

    private sealed class ImmediateProgress(Action<RecordingDiagnosticProgress> report) : IProgress<RecordingDiagnosticProgress>
    {
        public void Report(RecordingDiagnosticProgress value) => report(value);
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly RecordingDiagnosticTarget Target = new("Window", "Synthetic fixture",
            WindowHwnd: 42, WindowPid: 17, WindowTitle: "Synthetic fixture", WindowExecutable: "fixture.exe");
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Flick-Recording-Diagnostic-Tests", Guid.NewGuid().ToString("N"));
        internal AppSettings Settings { get; }
        internal RecordingDiagnosticPlan Plan { get; }
        internal FakeLease Lease { get; } = new();
        internal int FactoryCalls, Registrations;
        internal bool RefuseLease;
        internal ClipMetadata? RegisteredClip;
        internal Func<RecordingDiagnosticReplay, CancellationToken, Task<ClipMetadata>>? OnRegister;

        internal Fixture(int replaySeconds = 30)
        {
            Directory.CreateDirectory(Root);
            Settings = new() { OriginalsFolder = Path.Combine(Root, "Originals"), ReplayDurationSeconds = replaySeconds };
            Plan = RecordingDiagnosticPlan.Create(Settings, Target);
        }

        internal RecordingDiagnosticRunner Runner(RecordingDiagnosticOptions? options = null) => new(_ =>
        {
            FactoryCalls++;
            return RefuseLease ? null : Lease;
        }, async (plan, replay, token) =>
        {
            Assert.Same(Plan, plan);
            token.ThrowIfCancellationRequested();
            Registrations++;
            RegisteredClip = OnRegister != null ? await OnRegister(replay, token) : ValidClip(replay);
            return RegisteredClip;
        }, options: options ?? QuickOptions());

        internal ClipMetadata ValidClip(RecordingDiagnosticReplay replay) => new()
        {
            OriginalFilePath = replay.FilePath, DurationSeconds = 3,
            Width = Lease.Profile.Width, Height = Lease.Profile.Height, Fps = Lease.Profile.Fps
        };

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class FakeLease : IRecordingDiagnosticLease
    {
        internal CaptureEncodingProfile Profile { get; } = new("obs_x264", "CPU (x264)", true, 1280, 720, 30);
        internal CaptureHostStatus Status { get; } = new()
        {
            State = "Armed", IsArmed = true, TargetValid = true, BufferedSec = 4
        };
        internal int Starts, Saves, Stops, Disposals;
        internal string? OutputDirectory, LastSaveId, LastFile;
        internal Func<CancellationToken, Task<CaptureEncodingProfile>>? OnStart;
        internal Func<CancellationToken, Task<CaptureHostStatus>>? OnStatus;
        internal Func<string, CancellationToken, Task<RecordingDiagnosticReplay>>? OnSave;
        internal Func<CancellationToken, Task<bool>>? OnStop;

        public Task<CaptureEncodingProfile> StartAsync(RecordingDiagnosticPlan plan, string outputDirectory, CancellationToken cancellationToken)
        {
            Starts++;
            OutputDirectory = outputDirectory;
            return OnStart?.Invoke(cancellationToken) ?? Task.FromResult(Profile);
        }

        public Task<CaptureHostStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            OnStatus?.Invoke(cancellationToken) ?? Task.FromResult(Status);

        public Task<RecordingDiagnosticReplay> SaveAndWaitAsync(string saveId, CancellationToken cancellationToken)
        {
            Saves++;
            LastSaveId = saveId;
            return OnSave?.Invoke(saveId, cancellationToken) ?? Task.FromResult(WriteReplay(saveId));
        }

        internal RecordingDiagnosticReplay WriteReplay(string id)
        {
            LastFile = Path.Combine(OutputDirectory!, "Flick_" + id + ".mkv");
            File.WriteAllText(LastFile, "owned-controller-fixture-not-media");
            return new(id, LastFile, Array.AsReadOnly(new[] { "Silent Diagnostic Audio" }));
        }

        public Task<bool> StopAndConfirmExitAsync(CancellationToken cleanupToken)
        {
            Stops++;
            return OnStop?.Invoke(cleanupToken) ?? Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
