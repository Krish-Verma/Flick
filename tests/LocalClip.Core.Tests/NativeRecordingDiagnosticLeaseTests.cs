using System.Text.Json;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;

namespace LocalClip.Core.Tests;

public sealed class NativeRecordingDiagnosticLeaseTests
{
    private static RecordingDiagnosticPlan Plan() => RecordingDiagnosticPlan.Create(
        new AppSettings { OriginalsFolder = Path.Combine(Path.GetTempPath(), "Flick-Diagnostic-Fixture") },
        new("Window", "Synthetic fixture", WindowHwnd: 42, WindowPid: 23,
            WindowTitle: "Synthetic fixture", WindowExecutable: "fixture.exe"));

    [Fact]
    public async Task UnsupportedHelperIsClosedBeforeAnyRecordingRequest()
    {
        var host = new FakeHost { Capabilities = new(1, false, false) };
        await using var lease = new NativeRecordingDiagnosticLease(host);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.StartAsync(Plan(), Path.GetTempPath(), default));

        Assert.Equal(1, host.Starts);
        Assert.Equal(0, host.CaptureStarts);
        Assert.True(host.Aborts > 0);
        Assert.True(host.ExitWaits > 0);
    }

    [Fact]
    public async Task StartupFailurePreservesTheRealLocalEncoderOrSourceExplanation()
    {
        const string explanation = "No requested H.264 encoder is registered. Update GPU drivers or enable CPU compatibility.";
        var host = new FakeHost { AcceptStart = false, LastStartError = explanation };
        await using var lease = new NativeRecordingDiagnosticLease(host);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lease.StartAsync(Plan(), Path.GetTempPath(), default));

        Assert.Equal(explanation, failure.Message);
        Assert.True(host.Aborts > 0);
        Assert.True(host.ExitWaits > 0);
    }

    [Fact]
    public async Task CancellationDuringIdleHandshakeAwaitsOwnedExitAndNeverStartsCapture()
    {
        var host = new FakeHost { StallCapabilities = true, HoldExit = true };
        await using var lease = new NativeRecordingDiagnosticLease(host);
        using var cancellation = new CancellationTokenSource();
        var start = lease.StartAsync(Plan(), Path.GetTempPath(), cancellation.Token);
        Assert.Equal(1, host.Starts);

        cancellation.Cancel();
        Assert.True(host.Aborts > 0);
        Assert.False(start.IsCompleted);
        Assert.Equal(0, host.CaptureStarts);
        host.ConfirmExit();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.True(host.ExitWaits > 0);
    }

    [Fact]
    public async Task DisposeBeforeStartPreventsAnyLaterHelperSpawn()
    {
        var host = new FakeHost();
        var lease = new NativeRecordingDiagnosticLease(host);
        await lease.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.StartAsync(Plan(), Path.GetTempPath(), default));

        Assert.Equal(0, host.Starts);
        Assert.Equal(0, host.CaptureStarts);
    }

    [Fact]
    public async Task CancellationBetweenCapabilityResultAndCaptureCannotRespawnHelper()
    {
        using var cancellation = new CancellationTokenSource();
        var host = new FakeHost { OnCapabilities = cancellation.Cancel };
        await using var lease = new NativeRecordingDiagnosticLease(host);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.StartAsync(Plan(), Path.GetTempPath(), cancellation.Token));

        Assert.Equal(1, host.Starts);
        Assert.Equal(0, host.CaptureStarts);
    }

    [Fact]
    public async Task AcceptedAcknowledgementAndUnrelatedTerminalCannotReportASavedFile()
    {
        var host = new FakeHost();
        await using var lease = new NativeRecordingDiagnosticLease(host);
        await lease.StartAsync(Plan(), Path.GetTempPath(), default);
        var saving = lease.SaveAndWaitAsync("test-save", default);
        Assert.False(saving.IsCompleted);
        host.Saved("other-save");
        Assert.False(saving.IsCompleted);

        host.Saved("test-save");
        var saved = await saving;

        Assert.Equal("test-save", saved.SaveId);
        Assert.Equal("fixture.mkv", saved.FilePath);
        Assert.Equal(new[] { "Silent Diagnostic Audio" }, saved.TrackNames);
    }

    [Fact]
    public async Task RealMatchingTerminalOutranksAMissingAcknowledgement()
    {
        var host = new FakeHost { TerminalBeforeLostAck = true };
        await using var lease = new NativeRecordingDiagnosticLease(host);
        await lease.StartAsync(Plan(), Path.GetTempPath(), default);

        var saved = await lease.SaveAndWaitAsync("test-save", default);

        Assert.Equal("test-save", saved.SaveId);
        Assert.Equal(0, host.Aborts);
    }

    [Fact]
    public async Task LostAcknowledgementKeepsAnAcceptedSavePendingUntilItsRealTerminal()
    {
        var host = new FakeHost { LostAckBeforeTerminal = true };
        await using var lease = new NativeRecordingDiagnosticLease(host);
        await lease.StartAsync(Plan(), Path.GetTempPath(), default);

        var saving = lease.SaveAndWaitAsync("test-save", default);
        Assert.False(saving.IsCompleted);
        host.Saved("test-save");

        Assert.Equal("test-save", (await saving).SaveId);
        Assert.Equal(0, host.Aborts);
    }

    [Fact]
    public async Task CancellingAcceptedSaveClosesAndAwaitsOnlyItsOwnedHelper()
    {
        var host = new FakeHost();
        await using var lease = new NativeRecordingDiagnosticLease(host);
        await lease.StartAsync(Plan(), Path.GetTempPath(), default);
        host.HoldExit = true;
        using var cancellation = new CancellationTokenSource();
        var saving = lease.SaveAndWaitAsync("test-save", cancellation.Token);

        cancellation.Cancel();
        Assert.False(saving.IsCompleted);
        host.ConfirmExit();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
        Assert.True(host.Aborts > 0);
        Assert.True(host.ExitWaits > 0);
    }

    [Fact]
    public async Task CleanupUsesOneShutdownAndDoesNotClaimAnUnconfirmedExit()
    {
        var host = new FakeHost { HoldExit = true };
        var lease = new NativeRecordingDiagnosticLease(host);
        await lease.StartAsync(Plan(), Path.GetTempPath(), default);
        using var deadline = new CancellationTokenSource();
        deadline.Cancel();

        Assert.False(await lease.StopAndConfirmExitAsync(deadline.Token));
        await lease.DisposeAsync();

        Assert.Equal(1, host.Shutdowns);
        Assert.True(host.Aborts > 0);
    }

    [Fact]
    public async Task ConcurrentShutdownAndCancellationCannotInvertHostAndTerminalEventLocks()
    {
        var host = new FakeHost { InverseCleanupLocks = true };
        await using var lease = new NativeRecordingDiagnosticLease(host);
        await lease.StartAsync(Plan(), Path.GetTempPath(), default);
        using var cancellation = new CancellationTokenSource();
        var saving = lease.SaveAndWaitAsync("test-save", cancellation.Token);
        var cleanup = lease.StopAndConfirmExitAsync(default);
        await host.ShutdownOwnsLifecycle.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();

        Assert.True(await cleanup.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
        Assert.True(host.CallbackCompletedWithoutBreakingCycle,
            "Terminal callbacks must complete while host shutdown owns its lifecycle lock and abort is waiting for it.");
        Assert.Equal(1, host.Shutdowns);
    }

    [Fact]
    public void TransientRequestBindsTheConsentedWindowWithoutMutatingPreferences()
    {
        var settings = new AppSettings { TargetType = "Window", WindowHwnd = 42, EnableMicrophone = false };
        var options = new CaptureStartOptions(false, new(42, 23, "Synthetic fixture", "fixture.exe", @"C:\Synthetic\fixture.exe", 123456));

        var request = JsonSerializer.SerializeToElement(CaptureHostClient.CreateStartParameters(settings, options));

        Assert.False(request.GetProperty("enable_system_audio").GetBoolean());
        Assert.False(request.GetProperty("enable_mic").GetBoolean());
        Assert.Equal(23u, request.GetProperty("expected_window_pid").GetUInt32());
        Assert.Equal("Synthetic fixture", request.GetProperty("expected_window_title").GetString());
        Assert.Equal("fixture.exe", request.GetProperty("expected_window_exe").GetString());
        Assert.Equal(@"C:\Synthetic\fixture.exe", request.GetProperty("expected_window_path").GetString());
        Assert.Equal(123456ul, request.GetProperty("expected_window_creation_time").GetUInt64());
        Assert.Equal(42ul, settings.WindowHwnd);
        Assert.Equal("", settings.WindowTitle);
    }

    [Fact]
    public void DefaultRequestKeepsNormalSystemAudioAndExplicitMonitorIdentityWinsOverIndex()
    {
        var settings = new AppSettings { TargetType = "Monitor", MonitorIndex = 2 };
        var normal = JsonSerializer.SerializeToElement(CaptureHostClient.CreateStartParameters(settings));
        Assert.True(normal.GetProperty("enable_system_audio").GetBoolean());
        Assert.False(normal.TryGetProperty("monitor_id", out _));
        var consent = JsonSerializer.SerializeToElement(CaptureHostClient.CreateStartParameters(settings,
            new(ExpectedMonitorId: "display-id")));
        Assert.Equal("display-id", consent.GetProperty("monitor_id").GetString());
        Assert.Equal(2, consent.GetProperty("monitor_index").GetInt32());
    }

    [Fact]
    public async Task AdvancedOptionsCannotReachAnUnverifiedOrMissingHelper()
    {
        using var client = new CaptureHostClient(Path.Combine(Path.GetTempPath(), "must-not-launch.exe"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartCaptureAsync(new AppSettings(), new(false)));
        Assert.False(client.IsRunning);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"protocol_version\":\"2\",\"diagnostic_no_system_audio\":true,\"source_identity_binding\":true}")]
    [InlineData("{\"protocol_version\":2,\"diagnostic_no_system_audio\":\"true\",\"source_identity_binding\":true}")]
    public void MalformedCapabilitiesCannotAuthorizeEndpointAccess(string json) =>
        Assert.Throws<InvalidOperationException>(() => CaptureHostCapabilities.FromResponse(
            new IpcResponse { Ok = true, Result = JsonSerializer.Deserialize<JsonElement>(json) }));

    private sealed class FakeHost : INativeRecordingDiagnosticHost
    {
        public event Action<string, string, IReadOnlyList<string>>? ReplaySaved;
        public event Action<string?>? SaveFailed;
        public CaptureEncodingProfile? Profile { get; } = new("obs_x264", "CPU x264", true, 1280, 720, 30);
        public string LastStartError { get; init; } = "";
        public bool AcceptStart { get; init; } = true;
        public CaptureHostCapabilities Capabilities { get; init; } = new(2, true, true);
        public bool HasPendingReplaySave { get; private set; }
        public bool StallCapabilities { get; init; }
        public bool HoldExit { get; set; }
        public bool TerminalBeforeLostAck { get; init; }
        public bool LostAckBeforeTerminal { get; init; }
        public bool InverseCleanupLocks { get; init; }
        public bool CallbackCompletedWithoutBreakingCycle { get; private set; }
        public TaskCompletionSource ShutdownOwnsLifecycle { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _lifecycle = new();
        private readonly ManualResetEventSlim _abortAttempted = new();
        public Action? OnCapabilities { get; init; }
        private readonly TaskCompletionSource<CaptureHostCapabilities> _capabilities = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts, CaptureStarts, Aborts, Shutdowns, ExitWaits;
        public void Start() => Starts++;
        public Task<CaptureHostCapabilities> GetCapabilitiesAsync()
        {
            OnCapabilities?.Invoke();
            return StallCapabilities ? _capabilities.Task : Task.FromResult(Capabilities);
        }
        public Task<bool> StartCaptureAsync(AppSettings settings, CaptureStartOptions options)
        {
            CaptureStarts++;
            Assert.False(options.EnableSystemAudio);
            Assert.False(settings.EnableMicrophone);
            Assert.Equal(23u, options.ExpectedWindow!.ProcessId);
            return Task.FromResult(AcceptStart);
        }
        public Task<CaptureHostStatus> GetStatusAsync() => Task.FromResult(new CaptureHostStatus { State = "Armed", Profile = Profile });
        public Task<bool> SaveReplayAsync(string saveId)
        {
            HasPendingReplaySave = true;
            if (LostAckBeforeTerminal) return Task.FromException<bool>(new TimeoutException("Synthetic lost acknowledgement"));
            if (!TerminalBeforeLostAck) return Task.FromResult(true);
            Saved(saveId);
            return Task.FromException<bool>(new TimeoutException("Synthetic lost acknowledgement"));
        }
        public void Saved(string saveId) { ReplaySaved?.Invoke(saveId, "fixture.mkv", ["Silent Diagnostic Audio"]); HasPendingReplaySave = false; }
        public Task ShutdownAsync()
        {
            Shutdowns++;
            if (!InverseCleanupLocks) { if (!HoldExit) ConfirmExit(); return Task.CompletedTask; }
            return Task.Factory.StartNew(() =>
            {
                lock (_lifecycle)
                {
                    ShutdownOwnsLifecycle.TrySetResult();
                    if (!_abortAttempted.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("No cancellation arrived.");
                    // A watchdog releases the fake native lock even against the
                    // broken implementation, so this regression fails rather
                    // than leaving permanently deadlocked test threads.
                    var callback = Task.Factory.StartNew(() => SaveFailed?.Invoke(null), CancellationToken.None,
                        TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    CallbackCompletedWithoutBreakingCycle = callback.Wait(TimeSpan.FromSeconds(2));
                }
                ConfirmExit();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        public void Abort()
        {
            _abortAttempted.Set();
            lock (_lifecycle)
            {
                Aborts++;
                if (StallCapabilities) _capabilities.TrySetException(new IOException("Synthetic closed IPC"));
                SaveFailed?.Invoke(null);
                if (!HoldExit) ConfirmExit();
            }
        }
        public void ConfirmExit() => _exit.TrySetResult();
        public async Task<bool> WaitForExitAsync(CancellationToken cancellationToken)
        {
            ExitWaits++;
            if (_exit.Task.IsCompleted) return true;
            try { await _exit.Task.WaitAsync(cancellationToken); return true; }
            catch (OperationCanceledException) { return false; }
        }
        public void Dispose() { }
    }
}
