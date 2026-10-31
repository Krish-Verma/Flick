using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LocalClip.App.ViewModels;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using LocalClip.Testing;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Run in the existing STA Application. Actual VM/client startup and
/// pipe messages against a metadata-only helper; no Window.Loaded or recorder.</summary>
internal static class CaptureStartupAssertions
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        Assert.True(Application.Current.Dispatcher.CheckAccess());
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Application.Current.Dispatcher));
        try
        {
            foreach (bool background in new[] { false, true })
            {
                // The ordinary source-binding feature must not depend on the
                // unrelated optional no-system-audio diagnostic feature.
                using var pipe = new CaptureHostPipeFixture(diagnosticNoAudio: false);
                using var fixture = new VmFixture(pipe.CreateClient(), pipe.Root);
                var vm = fixture.Vm;
                string preferences = JsonSerializer.Serialize(vm.Settings);
                Assert.False(vm.CaptureHost.IsRunning);
                Pump(background ? vm.ApplyBackgroundReplayPreferencesAsync(true) : vm.StartCaptureAsync());
                fixture.StopTimers();
                Assert.Equal("Buffering", vm.CaptureStatus);
                Assert.False(vm.IsArmed);
                Assert.Equal(RecordingEngineState.Buffering, vm.RecordingHealth.EngineState);
                Assert.Equal(ReplaySaveState.Idle, vm.RecordingHealth.SaveState);
                Assert.Equal(CaptureHostHealth.Responsive, vm.RecordingHealth.HostHealth);
                Assert.Equal("fixture_h264", vm.RecordingHealth.Profile?.EncoderId);
                Assert.Null(vm.RecordingHealth.LastRecordingIssue);
                Assert.Equal(new[] { "get_capabilities", "start_capture" }, Commands(pipe));
                var parameters = pipe.Requests()[1].GetProperty("params");
                Assert.Equal("fixture-monitor-id", parameters.GetProperty("monitor_id").GetString());
                Assert.Equal(7, parameters.GetProperty("monitor_index").GetInt32());
                Assert.Equal("systemfallback", parameters.GetProperty("audio_mode").GetString());
                Assert.True(parameters.GetProperty("enable_system_audio").GetBoolean());
                Assert.False(parameters.GetProperty("enable_mic").GetBoolean());
                Assert.False(parameters.TryGetProperty("game_audio", out _));
                Assert.Equal(preferences, JsonSerializer.Serialize(vm.Settings));
                if (background) Assert.Equal("Replay active in the tray.", vm.GameDetectionStatus);
                Pump(vm.StopCaptureAsync());
                Assert.False(vm.CaptureHost.IsRunning);
            }

            const string refusal = "Fixture encoder refused: no compatible encoder initialized.";
            foreach (bool background in new[] { false, true })
            {
                using var pipe = new CaptureHostPipeFixture(refusal: refusal);
                using var fixture = new VmFixture(pipe.CreateClient(), pipe.Root);
                Pump(background ? fixture.Vm.ApplyBackgroundReplayPreferencesAsync(true) : fixture.Vm.StartCaptureAsync());
                fixture.StopTimers();
                AssertFailure(fixture.Vm, CaptureHostHealth.Responsive, refusal);
                Assert.Contains(refusal, fixture.Vm.GameDetectionStatus);
                if (background)
                {
                    Assert.True(fixture.Vm.HasAutomaticReplayStartFailure);
                    int before = pipe.Requests().Length;
                    Pump((Task)typeof(MainViewModel).GetMethod("TryStartBackgroundReplayAsync", Hidden)!.Invoke(fixture.Vm, null)!);
                    Assert.Equal(before, pipe.Requests().Length); // No timer-driven retry loop.
                }
                Guid failedSession = fixture.Vm.RecordingHealth.SessionId;
                pipe.AllowCapture();
                Pump(background ? fixture.Vm.ApplyBackgroundReplayPreferencesAsync(true) : fixture.Vm.StartCaptureAsync());
                fixture.StopTimers();
                Assert.NotEqual(failedSession, fixture.Vm.RecordingHealth.SessionId);
                Assert.Equal("Buffering", fixture.Vm.CaptureStatus);
                Assert.False(fixture.Vm.IsArmed);
                Assert.Equal(ReplaySaveState.Idle, fixture.Vm.RecordingHealth.SaveState);
                Assert.Equal("fixture_h264", fixture.Vm.RecordingHealth.Profile?.EncoderId);
                Assert.Equal(CaptureHostHealth.Responsive, fixture.Vm.RecordingHealth.HostHealth);
                Assert.Contains(refusal, fixture.Vm.HealthLastFailureText); // Retained history remains truthful.
                Assert.Equal(!background, (bool)typeof(MainViewModel).GetField("_manualReplayReserved", Hidden)!.GetValue(fixture.Vm)!);
                Assert.Equal(new[] { "get_capabilities", "start_capture", "get_capabilities", "start_capture" }, Commands(pipe));
                Pump(fixture.Vm.StopCaptureAsync());
            }

            using (var pipe = new CaptureHostPipeFixture(sourceIdentity: false))
            using (var fixture = new VmFixture(pipe.CreateClient(), pipe.Root))
            {
                Pump(fixture.Vm.StartCaptureAsync());
                AssertFailure(fixture.Vm, CaptureHostHealth.Responsive, "Update the capture helper before recording this exact display.");
                Assert.Equal(new[] { "get_capabilities" }, Commands(pipe));
            }

            using (var pipe = new CaptureHostPipeFixture())
            using (var fixture = new VmFixture(new CaptureHostClient(System.IO.Path.Combine(pipe.Root, "not-created.exe")), pipe.Root))
            {
                Pump(fixture.Vm.ApplyBackgroundReplayPreferencesAsync(true));
                fixture.StopTimers();
                AssertFailure(fixture.Vm, CaptureHostHealth.Exited, "CaptureHost executable not found");
                Assert.Contains("CaptureHost executable not found", fixture.Vm.GameDetectionStatus);
                Assert.Empty(pipe.Requests());
                Assert.False(fixture.Vm.CaptureHost.IsRunning);
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void AssertFailure(MainViewModel vm, CaptureHostHealth host, string message)
    {
        Assert.Equal("Faulted", vm.CaptureStatus);
        Assert.False(vm.IsArmed);
        Assert.Equal(RecordingEngineState.Faulted, vm.RecordingHealth.EngineState);
        Assert.Equal(ReplaySaveState.Idle, vm.RecordingHealth.SaveState);
        Assert.Equal(host, vm.RecordingHealth.HostHealth);
        Assert.Null(vm.RecordingHealth.Profile);
        Assert.Contains(message, vm.RecordingHealth.LastRecordingIssue?.Message);
        Assert.DoesNotContain("Host: Starting", vm.HealthDetailsText);
        Assert.False((bool)typeof(MainViewModel).GetField("_manualReplayReserved", Hidden)!.GetValue(vm)!);
        Assert.False((bool)typeof(MainViewModel).GetField("_backgroundReplayStarting", Hidden)!.GetValue(vm)!);
        Assert.False((bool)typeof(MainViewModel).GetField("_gameAutomationBusy", Hidden)!.GetValue(vm)!);
        Assert.True(vm.CanChangeCaptureSource);
    }

    private static string?[] Commands(CaptureHostPipeFixture pipe) => pipe.Requests()
        .Select(request => request.GetProperty("cmd").GetString()).ToArray();

    private static void Pump(Task task) => LibraryObservationAssertions.Pump(task.WaitAsync(TimeSpan.FromSeconds(12)));

    private sealed class VmFixture : IDisposable
    {
        public MainViewModel Vm { get; } = new();
        private readonly CaptureHostClient _originalHost;

        public VmFixture(CaptureHostClient client, string root)
        {
            StopTimers();
            LibraryObservationAssertions.Settle(Vm);
            _originalHost = Vm.CaptureHost;
            // Test-only instance substitution. No production transport bypass or
            // persistent settings mutation is added to the application.
            typeof(MainViewModel).GetField("_captureHost", Hidden)!.SetValue(Vm, client);
            typeof(MainViewModel).GetField("_settings", Hidden)!.SetValue(Vm, new AppSettings
            {
                TargetType = "Monitor", MonitorIndex = 7, MonitorDevice = "fixture-monitor-id",
                OriginalsFolder = System.IO.Path.Combine(root, "Originals"),
                ExportsFolder = System.IO.Path.Combine(root, "Exports"), TempFolder = System.IO.Path.Combine(root, "Temp")
            });
            var target = new TargetItem { Type = "Monitor", MonitorIndex = 7, MonitorDevice = "fixture-monitor-id", DisplayName = "Metadata fixture display" };
            Vm.Targets.Clear(); Vm.Targets.Add(target);
            typeof(MainViewModel).GetField("_selectedTarget", Hidden)!.SetValue(Vm, target);
            client.StateChanged += typeof(MainViewModel).GetMethod("OnHostStateChanged", Hidden)!.CreateDelegate<Action<string>>(Vm);
            client.CaptureConfigured += typeof(MainViewModel).GetMethod("OnCaptureConfigured", Hidden)!.CreateDelegate<Action<CaptureEncodingProfile>>(Vm);
            Assert.False(Vm.DiagnosticsInitialized);
        }

        public void StopTimers()
        {
            foreach (var field in typeof(MainViewModel).GetFields(Hidden))
                if (field.GetValue(Vm) is DispatcherTimer timer) timer.Stop();
        }

        public void Dispose()
        {
            StopTimers();
            try
            {
                if (Vm.CaptureHost.IsRunning) Pump(Vm.CaptureHost.ShutdownAsync());
            }
            finally { Vm.Dispose(); _originalHost.Dispose(); }
        }
    }
}
