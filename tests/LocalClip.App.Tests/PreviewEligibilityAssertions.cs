using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>
/// Runs in the existing unshown STA fixture. Cached availability is synthetic;
/// every source path deliberately does not exist, so even a failed guard cannot
/// assign a media source. No video, codec worker, clipboard, capture or OS keypress.
/// </summary>
internal static class PreviewEligibilityAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(MainWindow window, MainViewModel vm)
    {
        Assert.True(window.Dispatcher.CheckAccess());
        Assert.False(window.IsLoaded);
        Assert.False(vm.CaptureHost.IsRunning);
        LibraryObservationAssertions.Settle(vm);
        T Find<T>(string name) where T : class => Assert.IsAssignableFrom<T>(window.FindName(name));
        FieldInfo VmField(string name) => typeof(MainViewModel).GetField(name, Hidden)!;
        FieldInfo WindowField(string name) => typeof(MainWindow).GetField(name, Hidden)!;
        void SetVm(string name, object? value) => VmField(name).SetValue(vm, value);
        void SetWindow(string name, object? value) => WindowField(name).SetValue(window, value);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Hidden)!.Invoke(window, args);
        bool NativeAllowed() => (bool)typeof(MainWindow).GetProperty("NativePreviewAllowed", Hidden)!.GetValue(window)!;
        void Update(bool poster = false) => Call("UpdateSelectedAvailabilityUi", poster);
        var media = Find<MediaElement>("VideoPlayer");
        var playerTimer = Assert.IsType<DispatcherTimer>(WindowField("_playerTimer").GetValue(window));
        Assert.Null(media.Source);
        Assert.False(playerTimer.IsEnabled);
        Assert.Null(WindowField("_previewCancellation").GetValue(window));
        Assert.Null(VmField("_sharePreviewCancellation").GetValue(vm));
        Assert.False(vm.IsBackgroundWorkActive);
        Assert.False(vm.IsExporting);
        Assert.False(vm.IsImporting);
        Assert.False(vm.IsRecordingDiagnosticRunning);
        AssertWorkerPresentationNotification(window, vm);

        string[] vmNames = ["_selectedClip", "_captureStatus", "_isImporting", "_isExporting", "_isRecordingDiagnosticRunning",
            "_isBuildingAudioPreview", "_gameAutomationBusy", "_diagnosticCleanupUnconfirmed", "_isPreparingToQuit",
            "_isExportReady", "_readyExportFilePath", "_statusDetails", "_exportStatusMessage"];
        string[] windowNames = ["_previewCancellation", "_quitInProgress", "_updateInstallInProgress", "_playerIsPlaying"];
        var vmBefore = vmNames.ToDictionary(name => name, name => VmField(name).GetValue(vm));
        var windowBefore = windowNames.ToDictionary(name => name, name => WindowField(name).GetValue(window));
        var buttons = new[] { "BtnPlayPause", "BtnPlayPoster", "BtnBuildPreview", "BtnPreviewMode" }
            .ToDictionary(name => name, Find<Button>);
        var enabledBefore = buttons.ToDictionary(pair => pair.Key, pair => pair.Value.IsEnabled);
        var contentsBefore = buttons.ToDictionary(pair => pair.Key, pair => pair.Value.Content);
        string[] visualNames = ["EditorContextLayer", "AudioPanel", "TrimPanel", "SharePanel", "PreviewFallbackPanel",
            "PlayerPoster", "PlayerPosterHint", "WorkspacePageLayer", "RecordingHealthPanel"];
        var visuals = visualNames.ToDictionary(name => name, Find<FrameworkElement>);
        var visibilityBefore = visuals.ToDictionary(pair => pair.Key, pair => pair.Value.Visibility);
        object? tooltipBefore = buttons["BtnPlayPoster"].ToolTip;
        string previewIssueBefore = Find<TextBlock>("TxtPreviewIssue").Text;
        string previewHintBefore = Find<TextBlock>("TxtPreviewFallbackHint").Text;
        string preferencesBefore = JsonSerializer.Serialize(vm.Settings);
        string missing = Path.Combine(Path.GetTempPath(), "Flick-preview-eligibility-" + Guid.NewGuid().ToString("N"), "not-created.mkv");
        Assert.False(File.Exists(missing));
        var available = new ClipMetadata { Id = "preview-cached-available", OriginalFilePath = missing };
        Assert.True(available.ApplyAvailabilityObservation(missing, true));
        var offline = new ClipMetadata { Id = "preview-known-offline", OriginalFilePath = missing };
        Assert.True(offline.ApplyAvailabilityObservation(missing, false));
        var unknown = new ClipMetadata { Id = "preview-unknown", OriginalFilePath = missing };
        object originalMode = Enum.Parse(typeof(MainWindow).GetNestedType("PlayerView", BindingFlags.NonPublic)!, "Original");
        using var pending = new CancellationTokenSource();
        using var inputSource = new HwndSource(new HwndSourceParameters("Flick preview eligibility fixture")
        { Width = 1, Height = 1, WindowStyle = 0 });

        void Reset(ClipMetadata? clip = null)
        {
            SetVm("_selectedClip", clip ?? available);
            SetVm("_captureStatus", "Off");
            foreach (string name in new[] { "_isImporting", "_isExporting", "_isRecordingDiagnosticRunning", "_isBuildingAudioPreview",
                "_gameAutomationBusy", "_diagnosticCleanupUnconfirmed", "_isPreparingToQuit", "_isExportReady" }) SetVm(name, false);
            SetVm("_readyExportFilePath", "");
            SetWindow("_quitInProgress", false); SetWindow("_updateInstallInProgress", false);
            SetWindow("_previewCancellation", null); SetWindow("_playerIsPlaying", false);
            visuals["WorkspacePageLayer"].SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            visuals["RecordingHealthPanel"].SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            visuals["EditorContextLayer"].SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
            visuals["AudioPanel"].SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
            visuals["TrimPanel"].SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            visuals["SharePanel"].SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            Update(poster: true);
        }
        void AssertNoMedia()
        {
            Assert.Null(media.Source);
            Assert.False(playerTimer.IsEnabled);
            Assert.False((bool)WindowField("_playerIsPlaying").GetValue(window)!);
            Assert.False(vm.CaptureHost.IsRunning);
        }
        void AssertOpenContext()
        {
            Assert.Equal(Visibility.Visible, visuals["EditorContextLayer"].Visibility);
            Assert.Equal(Visibility.Visible, visuals["AudioPanel"].Visibility);
        }
        void AssertControls(bool native, bool transport, bool build)
        {
            Assert.Equal(native, NativeAllowed());
            Assert.Equal(transport, buttons["BtnPlayPause"].IsEnabled);
            Assert.Equal(transport, buttons["BtnPlayPoster"].IsEnabled);
            Assert.Equal(native, buttons["BtnPreviewMode"].IsEnabled);
            Assert.Equal(build, buttons["BtnBuildPreview"].IsEnabled);
            AssertNoMedia(); AssertOpenContext();
        }
        void AssertBlockedEntries()
        {
            const string sentinel = "Synthetic blocked preview sentinel";
            vm.StatusDetails = sentinel;
            Call("SetPreviewSource", missing, originalMode, true);
            Assert.Equal(sentinel, vm.StatusDetails); // Blocked before even the missing-file fallback.
            AssertNoMedia();
            Call("BtnPlayPause_Click", buttons["BtnPlayPause"], new RoutedEventArgs());
            Assert.Equal(sentinel, vm.StatusDetails);
            AssertNoMedia();
            // Dispatch the Window's real Space route too. Unshown WPF controls
            // cannot reliably acquire native focus; the guarded endpoint above
            // independently covers the handler called by that route.
            var space = new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, Key.Space)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            Call("MainWindow_PreviewKeyDown", window, space);
            AssertNoMedia();
            // Missing paths keep these menu handlers safe even if a later edit
            // regresses their shared source guard. No fake playable file exists.
            Call("PreviewOriginal_Click", buttons["BtnPreviewMode"], new RoutedEventArgs());
            AssertNoMedia(); AssertOpenContext();
            SetVm("_isExportReady", true); SetVm("_readyExportFilePath", missing);
            Call("PreviewExport_Click", buttons["BtnPreviewMode"], new RoutedEventArgs());
            AssertNoMedia(); AssertOpenContext();
            SetVm("_isExportReady", false); SetVm("_readyExportFilePath", "");
        }

        try
        {
            Reset(); AssertControls(native: true, transport: true, build: true);
            SetVm("_captureStatus", "Armed"); Update();
            AssertControls(native: true, transport: true, build: false);
            Reset(); SetVm("_isImporting", true); Update();
            AssertControls(native: true, transport: true, build: false);
            Reset(); SetVm("_isExporting", true); Update();
            AssertControls(native: true, transport: true, build: false);

            Reset(); SetVm("_captureStatus", "Starting"); Update();
            AssertControls(native: false, transport: false, build: false);
            AssertBlockedEntries();
            Reset(); SetVm("_isRecordingDiagnosticRunning", true); Update();
            AssertControls(native: false, transport: false, build: false);
            AssertBlockedEntries();

            Reset(); SetWindow("_previewCancellation", pending);
            vm.SetBuildingAudioPreview(true);
            AssertControls(native: true, transport: false, build: false);
            // The VM finishes first; the shell still owns its CTS at this point.
            vm.SetBuildingAudioPreview(false);
            AssertControls(native: true, transport: false, build: false);
            var focusScope = FocusManager.GetFocusScope(window);
            var logicalFocusBeforeCancel = FocusManager.GetFocusedElement(focusScope);
            Call("CancelPendingPreview");
            Assert.Same(logicalFocusBeforeCancel, FocusManager.GetFocusedElement(focusScope));
            Assert.True(pending.IsCancellationRequested);
            Assert.Null(WindowField("_previewCancellation").GetValue(window));
            AssertControls(native: true, transport: true, build: true);

            Reset(offline);
            Assert.True(offline.HasKnownAvailability);
            AssertControls(native: false, transport: false, build: false);
            Assert.Equal("Original video is offline", Find<TextBlock>("TxtPreviewIssue").Text);
            AssertBlockedEntries();
            Reset(unknown);
            Assert.False(unknown.HasKnownAvailability);
            AssertControls(native: false, transport: false, build: false);
            Assert.Equal("Checking original…", Find<TextBlock>("TxtPreviewIssue").Text);
            AssertBlockedEntries();
            Assert.Equal(preferencesBefore, JsonSerializer.Serialize(vm.Settings));
            Assert.False(window.IsLoaded);
        }
        finally
        {
            foreach (var pair in vmBefore) SetVm(pair.Key, pair.Value);
            foreach (var pair in windowBefore) SetWindow(pair.Key, pair.Value);
            // Worker-state notifications also update controls outside the player.
            // Restore those bindings without issuing a new selection/read request.
            foreach (string name in new[] { nameof(MainViewModel.StatusDetails), nameof(MainViewModel.IsBackgroundWorkActive),
                nameof(MainViewModel.CanEditClip), nameof(MainViewModel.CanStartExport) })
                typeof(MainViewModel).GetMethod("OnPropertyChanged", Hidden)!.Invoke(vm, [name]);
            foreach (var pair in visibilityBefore) visuals[pair.Key].SetCurrentValue(UIElement.VisibilityProperty, pair.Value);
            foreach (var pair in enabledBefore) buttons[pair.Key].SetCurrentValue(UIElement.IsEnabledProperty, pair.Value);
            foreach (var pair in contentsBefore) buttons[pair.Key].SetCurrentValue(ContentControl.ContentProperty, pair.Value);
            buttons["BtnPlayPoster"].ToolTip = tooltipBefore;
            Find<TextBlock>("TxtPreviewIssue").Text = previewIssueBefore;
            Find<TextBlock>("TxtPreviewFallbackHint").Text = previewHintBefore;
        }
    }

    private static void AssertWorkerPresentationNotification(MainWindow window, MainViewModel vm)
    {
        // Flush earlier fixture notifications before installing the stale text;
        // only this worker's notification may satisfy the repaint assertion.
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        // Raise the production VM event, so an absent/unsubscribed callback
        // cannot pass merely because a worker returned without touching UI.
        var notify = Assert.IsAssignableFrom<MethodInfo>(typeof(MainViewModel)
            .GetMethod("OnPropertyChanged", Hidden, null, [typeof(string)], null));
        var caption = Assert.IsType<TextBlock>(window.FindName("TxtCaptureStatus"));
        string before = caption.Text;
        string expected = vm.CaptureStateText;
        const string stale = "unrefreshed fixture";
        Assert.NotEqual(stale, expected);
        int observed = 0, observedOnWorker = 0;
        PropertyChangedEventHandler observer = (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.CaptureStateText)) return;
            Interlocked.Increment(ref observed);
            if (!window.Dispatcher.CheckAccess()) Interlocked.Increment(ref observedOnWorker);
        };
        vm.PropertyChanged += observer;
        try
        {
            caption.SetCurrentValue(TextBlock.TextProperty, stale);
            var worker = Task.Run(() =>
            {
                Assert.False(window.Dispatcher.CheckAccess());
                notify.Invoke(vm, [nameof(MainViewModel.CaptureStateText)]);
            });
            // Returning from an event notification must not require the UI
            // thread to block on that worker or install a global SyncContext.
            Assert.True(SpinWait.SpinUntil(() => worker.IsCompleted, TimeSpan.FromSeconds(3)),
                "Worker presentation notification did not return within three seconds.");
            Assert.False(worker.IsFaulted, worker.Exception?.ToString());
            worker.GetAwaiter().GetResult();
            Assert.Equal(1, Volatile.Read(ref observed));
            Assert.Equal(1, Volatile.Read(ref observedOnWorker));
            Assert.Equal(stale, caption.Text); // Manual UI work must still be queued.
            var elapsed = Stopwatch.StartNew();
            while (caption.Text != expected && elapsed.Elapsed < TimeSpan.FromSeconds(3))
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Thread.Sleep(1);
            }
            Assert.Equal(expected, caption.Text); // Proves the real subscribed UI callback ran.
            Assert.Equal(expected, vm.CaptureStateText); // No model state was changed.
            Assert.False(window.IsLoaded);
            Assert.False(vm.CaptureHost.IsRunning);
            Assert.Null(Assert.IsType<MediaElement>(window.FindName("VideoPlayer")).Source);
        }
        finally
        {
            vm.PropertyChanged -= observer;
            caption.SetCurrentValue(TextBlock.TextProperty, before);
        }
    }
}
