using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using LocalClip.App.ViewModels;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Storage;
using LocalClip.Testing;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Owned metadata and held media operations exercise persistence and
/// late completions. No real media tools, device, capture, or profile writes.</summary>
internal static class LibraryMutationAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Application.Current.Dispatcher));
        try
        {
            SourcePersistenceFailureKeepsLiveSource();
            foreach (bool unlink in new[] { false, true }) ExportCompletionCannotRestoreRemovedClip(unlink);
            foreach (bool unlink in new[] { false, true }) PendingAudioCannotRestoreRemovedClip(unlink);
            foreach (bool quit in new[] { false, true }) RescanCleanupFinishesBeforeCaptureOrQuit(quit);
            RescanCleanupTimeoutRefusesCaptureUntilExplicitRetry();
            SettledUnconfirmedScanBlocksRecordingButAllowsQuit();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void SourcePersistenceFailureKeepsLiveSource()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        var old = new TargetItem { Type = "Monitor", MonitorIndex = 7, MonitorDevice = "fixture-monitor-id", DisplayName = "Old display" };
        var next = new TargetItem { Type = "Monitor", MonitorIndex = 8, MonitorDevice = "different-display", DisplayName = "New display" };
        vm.Targets.Clear(); vm.Targets.Add(old); vm.Targets.Add(next); Set(vm, "_selectedTarget", old);
        fixture.Client.StateChanged += RequireMethod(typeof(MainViewModel), "OnHostStateChanged").CreateDelegate<Action<string>>(vm);
        fixture.Client.CaptureConfigured += RequireMethod(typeof(MainViewModel), "OnCaptureConfigured").CreateDelegate<Action<CaptureEncodingProfile>>(vm);
        Set(vm, "_captureHost", fixture.Client);
        string saved = Path.Combine(fixture.Root, "settings.json");
        vm.Settings.SaveToFile(saved); byte[] before = File.ReadAllBytes(saved);
        Set(vm, "_persistSourceSelection", (Action<AppSettings>)(_ => throw new IOException("Owned fixture disk failure")));
        vm.ConfirmTargetReset = () => true;
        Pump(vm.StartCaptureAsync()); fixture.StopTimers();
        Assert.True(vm.IsCaptureActive);
        int commands = fixture.Pipe.Requests().Length;
        vm.SelectedTarget = next; // Actual bound setter must contain this write failure.
        Assert.Same(old, vm.SelectedTarget);
        Assert.Equal("fixture-monitor-id", vm.Settings.MonitorDevice);
        Assert.Equal(7, vm.Settings.MonitorIndex);
        Assert.True(vm.IsCaptureActive);
        Assert.Equal("Buffering", vm.CaptureStatus);
        Assert.Equal("Old display", vm.RecordingHealth.Source?.Name);
        Assert.Contains("Source change was not saved", vm.StatusDetails);
        Assert.Equal(commands, fixture.Pipe.Requests().Length);
        Assert.Equal(before, File.ReadAllBytes(saved));
        Pump(vm.StopCaptureAsync());
    }

    private static void ExportCompletionCannotRestoreRemovedClip(bool unlink)
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        string source = Path.Combine(fixture.Root, "source.mkv");
        byte[] original = [1, 2, 3, 4]; File.WriteAllBytes(source, original);
        var folder = fixture.Storage.ConnectFolder(fixture.Root);
        string thumbnail = Path.Combine(fixture.Root, "existing.jpg"); File.WriteAllBytes(thumbnail, [1]);
        var clip = new ClipMetadata { OriginalFilePath = source, DurationSeconds = 10, TrimEndSeconds = 10,
            Width = 1920, Height = 1080, ThumbnailPath = thumbnail, SourceKind = "Imported", ConnectedFolderId = folder.Id,
            AudioTracks = [new AudioTrackInfo { TrackIndex = 0, Codec = "aac" }] };
        fixture.Storage.AddOrUpdateClip(clip);
        vm.ReloadClips(); vm.SelectedClip = clip; LibraryObservationAssertions.Settle(vm);
        var held = new HeldExporter(); Set(vm, "_exporter", held);
        Assert.True(vm.CanStartExport);
        Task export = vm.ExportForDiscordAsync();
        Until(() => held.Started.Task.IsCompleted);
        try
        {
            if (unlink) Assert.True(vm.RemoveConnectedFolder(folder.Id));
            else { Assert.True(fixture.Storage.DeleteClip(clip.Id)); vm.ReloadClips(); }
            held.Release.TrySetResult(); Pump(export);
            Assert.Empty(fixture.Storage.GetAllClips());
            Assert.Empty(new ClipStorage(vm.Settings, metadataDirectory: fixture.Metadata).GetAllClips());
            Assert.Equal(original, File.ReadAllBytes(source));
            Assert.False(vm.IsExportReady);
            Assert.False(clip.IsExportReady);
            Assert.DoesNotContain(vm.RecentExports, item => item.ClipId == clip.Id);
        }
        finally { held.Release.TrySetResult(); Pump(export); }
    }

    private static void PendingAudioCannotRestoreRemovedClip(bool unlink)
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        string source = Path.Combine(fixture.Root, "source.mkv"), thumbnail = Path.Combine(fixture.Root, "existing.jpg");
        byte[] original = [7, 8, 9]; File.WriteAllBytes(source, original); File.WriteAllBytes(thumbnail, [1]);
        var folder = fixture.Storage.ConnectFolder(fixture.Root);
        var track = new AudioTrackInfo { TrackIndex = 0, Codec = "aac" };
        var clip = new ClipMetadata { OriginalFilePath = source, DurationSeconds = 10, TrimEndSeconds = 10,
            ThumbnailPath = thumbnail, SourceKind = "Imported", ConnectedFolderId = folder.Id, AudioTracks = [track] };
        fixture.Storage.AddOrUpdateClip(clip);
        vm.ReloadClips(); vm.SelectedClip = clip; LibraryObservationAssertions.Settle(vm);
        Assert.True(clip.IsAvailableForUi);
        track.Gain = .5; // Actual subscribed track event schedules the production debounce.
        var timer = (DispatcherTimer)Get(vm, "_audioEditCommitTimer");
        Assert.True(timer.IsEnabled); timer.Stop();
        Assert.Same(clip, Get(vm, "_pendingAudioClip"));
        if (unlink) Assert.True(vm.RemoveConnectedFolder(folder.Id));
        else
        {
            Assert.True(fixture.Storage.DeleteClip(clip.Id));
            vm.SelectedClip = null; // This production setter flushes the old pending audio before changing selection.
            vm.ReloadClips();
        }
        vm.FlushPendingAudioEdits(); // A later timer/dispose flush is harmless too.
        Assert.Null(vm.SelectedClip);
        Assert.Empty(vm.FilteredClips);
        Assert.Empty(fixture.Storage.GetAllClips());
        Assert.Empty(new ClipStorage(vm.Settings, metadataDirectory: fixture.Metadata).GetAllClips());
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.False(timer.IsEnabled);
        Assert.Null(typeof(MainViewModel).GetField("_pendingAudioClip", Hidden)!.GetValue(vm));
    }

    private static void RescanCleanupFinishesBeforeCaptureOrQuit(bool quit)
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        var held = new HeldProbe();
        Set(vm, "_storage", new ClipStorage(vm.Settings, held, fixture.Metadata));
        File.WriteAllBytes(Path.Combine(vm.Settings.OriginalsFolder, "pending.mkv"), [1, 2]);
        var target = new TargetItem { Type = "Monitor", MonitorIndex = 7, MonitorDevice = "fixture-monitor-id", DisplayName = "Owned monitor" };
        vm.Targets.Clear(); vm.Targets.Add(target); Set(vm, "_selectedTarget", target);
        fixture.Client.StateChanged += RequireMethod(typeof(MainViewModel), "OnHostStateChanged").CreateDelegate<Action<string>>(vm);
        fixture.Client.CaptureConfigured += RequireMethod(typeof(MainViewModel), "OnCaptureConfigured").CreateDelegate<Action<CaptureEncodingProfile>>(vm);
        Set(vm, "_captureHost", fixture.Client);
        Task scan = vm.RescanLibraryAsync();
        Until(() => held.Started.Task.IsCompleted);
        Task transition = quit ? vm.PrepareToQuitAsync() : vm.StartCaptureAsync();
        try
        {
            Until(() => held.Cancelled.Task.IsCompleted);
            Assert.False(transition.IsCompleted);
            Assert.Empty(fixture.Pipe.Requests()); // Not even capability negotiation precedes confirmed cleanup.
            held.Release.TrySetResult(); Pump(scan); Pump(transition); fixture.StopTimers();
            Assert.True(held.Exited);
            Assert.False(vm.IsBackgroundWorkActive);
            if (quit) Assert.True(((Task<bool>)transition).GetAwaiter().GetResult());
            else
            {
                Assert.Equal("Buffering", vm.CaptureStatus);
                Assert.False(vm.IsArmed);
                Assert.Equal(new[] { "get_capabilities", "start_capture" }, fixture.Pipe.Requests().Select(item => item.GetProperty("cmd").GetString()).ToArray());
                Pump(vm.StopCaptureAsync());
            }
        }
        finally { held.Release.TrySetResult(); Pump(scan); Pump(transition); }
    }

    private static void RescanCleanupTimeoutRefusesCaptureUntilExplicitRetry()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        var held = new HeldProbe();
        Set(vm, "_storage", new ClipStorage(vm.Settings, held, fixture.Metadata));
        File.WriteAllBytes(Path.Combine(vm.Settings.OriginalsFolder, "pending.mkv"), [1]);
        var target = new TargetItem { Type = "Monitor", MonitorDevice = "fixture-monitor-id", MonitorIndex = 7, DisplayName = "Owned monitor" };
        vm.Targets.Clear(); vm.Targets.Add(target); Set(vm, "_selectedTarget", target);
        fixture.Client.StateChanged += RequireMethod(typeof(MainViewModel), "OnHostStateChanged").CreateDelegate<Action<string>>(vm);
        fixture.Client.CaptureConfigured += RequireMethod(typeof(MainViewModel), "OnCaptureConfigured").CreateDelegate<Action<CaptureEncodingProfile>>(vm);
        Set(vm, "_captureHost", fixture.Client);
        Task scan = vm.RescanLibraryAsync(); Until(() => held.Started.Task.IsCompleted);
        try
        {
            Pump(vm.StartCaptureAsync()); // Actual 3-second cleanup deadline must refuse capture.
            fixture.StopTimers();
            Assert.Equal("Faulted", vm.CaptureStatus);
            Assert.Contains("Library rescan work is still stopping", vm.RecordingHealth.LastRecordingIssue?.Message);
            Assert.Empty(fixture.Pipe.Requests());
            Assert.False(scan.IsCompleted);
            held.Release.TrySetResult(); Pump(scan);
            Assert.True(held.Exited);
            Assert.Empty(fixture.Pipe.Requests()); // Cleanup does not automatically retry capture.
            Pump(vm.StartCaptureAsync()); fixture.StopTimers();
            Assert.Equal("Buffering", vm.CaptureStatus);
            Pump(vm.StopCaptureAsync());
        }
        finally { held.Release.TrySetResult(); Pump(scan); }
    }

    private static void SettledUnconfirmedScanBlocksRecordingButAllowsQuit()
    {
        using var fixture = new Fixture();
        var vm = fixture.Vm;
        Set(vm, "_storage", new ClipStorage(vm.Settings, new UnconfirmedProbe(), fixture.Metadata));
        Set(vm, "_captureHost", fixture.Client);
        File.WriteAllBytes(Path.Combine(vm.Settings.OriginalsFolder, "pending.mkv"), [1]);
        var target = new TargetItem { Type = "Monitor", MonitorDevice = "fixture-monitor-id", DisplayName = "Owned monitor" };
        vm.Targets.Clear(); vm.Targets.Add(target); Set(vm, "_selectedTarget", target);
        Pump(vm.RescanLibraryAsync());
        Assert.True(vm.IsBackgroundWorkActive);
        Assert.False(vm.CanRunRecordingDiagnostic);
        Pump(vm.StartCaptureAsync());
        Assert.Equal("Faulted", vm.CaptureStatus);
        Assert.Empty(fixture.Pipe.Requests());
        Task<bool> quit = vm.PrepareToQuitAsync(); Pump(quit);
        Assert.True(quit.GetAwaiter().GetResult());
        Assert.Empty(fixture.Pipe.Requests());
    }

    private sealed class UnconfirmedProbe : MediaProber
    {
        public override Task<ProbeResult> ProbeFileAsync(string file, CancellationToken ct = default) =>
            Task.FromException<ProbeResult>(new LocalClip.Core.Audio.MediaWorkerCleanupException());
    }

    private sealed class HeldExporter : ClipExporter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<bool> RunFfmpegEncodeAsync(string input, string output, double start,
            double duration, BitrateBudget budget, string audioFilter, bool software, CancellationToken ct)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(8), ct);
            await File.WriteAllBytesAsync(output, new byte[1024], ct); return true;
        }
        protected override Task<ProbeResult> ProbeExportAsync(string path, CancellationToken ct) => Task.FromResult(new ProbeResult
        { IsValid = true, VideoCodec = "h264", DurationSeconds = 10, Width = 1920, Height = 1080,
            AudioTracks = [new AudioTrackInfo { Codec = "aac" }] });
        protected override Task<bool> VerifyFullDecodeAsync(string path, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class HeldProbe : MediaProber
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Exited { get; private set; }
        public override async Task<ProbeResult> ProbeFileAsync(string file, CancellationToken ct = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(8));
                Exited = true; throw;
            }
            throw new InvalidOperationException("Owned probe must be cancelled.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public CaptureHostPipeFixture Pipe { get; } = new();
        public string Root => Pipe.Root;
        public string Metadata => Path.Combine(Root, "Metadata");
        public MainViewModel Vm { get; }
        public ClipStorage Storage => (ClipStorage)Get(Vm, "_storage");
        public CaptureHostClient Client { get; }
        private readonly CaptureHostClient _original;
        public Fixture()
        {
            var seed = new AppSettings { OriginalsFolder = Path.Combine(Root, "Originals"), ExportsFolder = Path.Combine(Root, "Exports"),
                TempFolder = Path.Combine(Root, "Temp"), MonitorIndex = 7, MonitorDevice = "fixture-monitor-id", DiscordTargetSizeMb = 2,
                ShowClipSavedOverlay = false, PlayClipSavedSound = false };
            foreach (string path in new[] { seed.OriginalsFolder, seed.ExportsFolder, seed.TempFolder }) Directory.CreateDirectory(path);
            var constructor = typeof(MainViewModel).GetConstructor(Hidden, null, [typeof(AppSettings), typeof(string)], null)
                ?? throw new InvalidOperationException("Owned MainViewModel(AppSettings, metadataDirectory) constructor is missing.");
            Vm = (MainViewModel)constructor.Invoke([seed, Metadata]);
            StopTimers(); LibraryObservationAssertions.Settle(Vm);
            _original = Vm.CaptureHost; Client = Pipe.CreateClient();
        }
        public void StopTimers() { foreach (var field in typeof(MainViewModel).GetFields(Hidden)) if (field.GetValue(Vm) is DispatcherTimer timer) timer.Stop(); }
        public void Dispose() { StopTimers(); Vm.Dispose(); Client.Dispose(); _original.Dispose(); Pipe.Dispose(); }
    }
    private static object Get(object value, string name) => value.GetType().GetField(name, Hidden)!.GetValue(value)!;
    private static void Set(object value, string name, object? data) => value.GetType().GetField(name, Hidden)!.SetValue(value, data);
    private static MethodInfo RequireMethod(Type type, string name) =>
        type.GetMethod(name, Hidden | BindingFlags.Public)
        ?? throw new InvalidOperationException($"Required fixture production handler {type.Name}.{name} is missing.");
    private static void Pump(Task task) => LibraryObservationAssertions.Pump(task.WaitAsync(TimeSpan.FromSeconds(12)));
    private static void Until(Func<bool> complete) => LibraryObservationAssertions.PumpUntil(complete, Application.Current.Dispatcher);
}
