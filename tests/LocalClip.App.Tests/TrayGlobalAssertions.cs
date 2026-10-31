using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalClip.App.Hotkeys;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using LocalClip.Testing;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Production hidden-window routes with an owned metadata-only pipe.
/// No visible NotifyIcon, real global key, recorder, GPU, or saved user setting.</summary>
internal static class TrayGlobalAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        var previous = SynchronizationContext.Current;
        MainWindow? window = null;
        MainViewModel? vm = null;
        CaptureHostClient? original = null;
        HotkeyManager? keys = null;
        using var pipe = new CaptureHostPipeFixture(diagnosticNoAudio: false);
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Application.Current.Dispatcher));
            var seed = new AppSettings { TargetType = "Monitor", MonitorIndex = 7,
                MonitorDevice = "fixture-monitor-id", OriginalsFolder = System.IO.Path.Combine(pipe.Root, "Originals"),
                ExportsFolder = System.IO.Path.Combine(pipe.Root, "Exports"), TempFolder = System.IO.Path.Combine(pipe.Root, "Temp"),
                ShowClipSavedOverlay = false, PlayClipSavedSound = false };
            foreach (string folder in new[] { seed.OriginalsFolder, seed.ExportsFolder, seed.TempFolder })
                System.IO.Directory.CreateDirectory(folder);
            // Seed both settings and metadata before either constructor. Windows
            // known-folder resolution is never redirected or consulted here.
            vm = (MainViewModel)typeof(MainViewModel).GetConstructor(Hidden, null,
                [typeof(AppSettings), typeof(string)], null)!.Invoke([seed, System.IO.Path.Combine(pipe.Root, "Metadata")]);
            window = (MainWindow)typeof(MainWindow).GetConstructor(Hidden, null,
                [typeof(MainViewModel)], null)!.Invoke([vm]);
            // App.Shutdown must not run close-to-tray against a disposed fixture.
            window.Closing -= typeof(MainWindow).GetMethod("Window_Closing", Hidden)!
                .CreateDelegate<System.ComponentModel.CancelEventHandler>(window);
            original = vm.CaptureHost;
            keys = (HotkeyManager)Field(window, "_hotkeyManager");
            StopTimers(vm);
            LibraryObservationAssertions.Settle(vm);
            var client = pipe.CreateClient();
            Set(vm, "_captureHost", client);
            var target = new TargetItem { Type = "Monitor", MonitorIndex = 7, MonitorDevice = "fixture-monitor-id", DisplayName = "Owned tray fixture" };
            vm.Targets.Clear(); vm.Targets.Add(target); Set(vm, "_selectedTarget", target);
            client.StateChanged += typeof(MainViewModel).GetMethod("OnHostStateChanged", Hidden)!.CreateDelegate<Action<string>>(vm);
            client.CaptureConfigured += typeof(MainViewModel).GetMethod("OnCaptureConfigured", Hidden)!.CreateDelegate<Action<CaptureEncodingProfile>>(vm);

            // Actual edit/cancel handlers preserve the existing draft and release
            // the transient suspension even without a keyboard-focus event.
            var settings = new SettingsWindow(vm.Settings, () => vm.IsCaptureActive,
                loadAudioEndpoints: _ => [], saveReviewedSettings: _ => throw new InvalidOperationException("No settings write authorized in fixture."),
                applyWindowsStartup: (_, _) => throw new InvalidOperationException("No startup change authorized in fixture."),
                readWindowsStartupStatus: _ => new WindowsStartupStatus(WindowsStartupRegistrationState.Registered, "Owned fixture"));
            Set(window, "_settingsPage", settings);
            settings.ShortcutEditingChanged += editing => keys.IsShortcutEditing = editing;
            string chord = ((TextBox)settings.FindName("TxtToggleHotkey")).Text;
            Invoke(settings, "ChangeShortcut_Click", new Button { Tag = "Toggle" }, new RoutedEventArgs());
            Assert.True(keys.IsShortcutEditing);
            Invoke(window, "PrepareWindowForBackground");
            Assert.False(keys.IsShortcutEditing);
            Assert.Equal(chord, ((TextBox)settings.FindName("TxtToggleHotkey")).Text);
            Assert.Equal(Visibility.Collapsed, ((StackPanel)settings.FindName("ShortcutEditor")).Visibility);
            Assert.False(vm.CaptureHost.IsRunning); // Hiding never starts capture by itself.
            Set(window, "_settingsPage", null); settings.LeavePage();

            // Synthetic IDs enter the actual WM_HOTKEY hook; no OS registration
            // or keyboard injection is performed by this fixture.
            Set(keys, "_toggleId", 19001); Set(keys, "_saveId", 19002);
            using var menu = (System.Windows.Forms.ContextMenuStrip)Invoke(window, "CreateTrayMenu")!;
            var replay = (System.Windows.Forms.ToolStripMenuItem)Field(window, "_trayReplayItem");
            var save = (System.Windows.Forms.ToolStripMenuItem)Field(window, "_traySaveItem");
            Assert.Contains(menu.Items.Cast<System.Windows.Forms.ToolStripItem>(), item => item.Text == "Open Flick");
            Assert.Contains(menu.Items.Cast<System.Windows.Forms.ToolStripItem>(), item => item.Text == "Settings");
            Assert.Contains(menu.Items.Cast<System.Windows.Forms.ToolStripItem>(), item => item.Text == "Quit Flick");
            Assert.False(save.Enabled);
            Assert.False(window.IsVisible);
            Hotkey(keys, 19001);
            Until(() => vm.CaptureStatus == "Buffering" && vm.CaptureHost.CurrentProfile != null && vm.RecordingHealth.Profile != null); StopTimers(vm);
            Assert.False(window.IsVisible);
            Assert.Equal("Stop replay", replay.Text);
            Assert.False(save.Enabled); // Accepted startup is not replay-ready evidence.

            int before = pipe.Requests().Length;
            Set(vm, "_saveRequestOperations", 1);
            Hotkey(keys, 19001); Drain();
            Assert.Equal(before, pipe.Requests().Length); // Global toggle cannot interrupt pending save.
            Set(vm, "_saveRequestOperations", 0);
            Set(vm, "_isRecordingDiagnosticRunning", true);
            Hotkey(keys, 19001); Hotkey(keys, 19002); Drain();
            Assert.Equal(before, pipe.Requests().Length); // Diagnostic reservation applies even when hidden.
            Set(vm, "_isRecordingDiagnosticRunning", false);

            var health = (RecordingHealthLedger)Field(vm, "_health");
            Guid session = (Guid)Field(vm, "_healthSession");
            health.ObserveBuffer(session, 3, 30, estimated: false);
            health.ObserveEngine(session, RecordingEngineState.Ready);
            vm.CaptureStatus = "Armed"; vm.IsArmed = true;
            Invoke(window, "UpdateStatusBadgeColor");
            Assert.True(save.Enabled);
            Hotkey(keys, 19002);
            Until(() => pipe.Requests().Any(request => request.GetProperty("cmd").GetString() == "save_replay") && !vm.IsReplaySavePending);
            Assert.True(vm.IsCaptureActive); // Metadata helper refuses saves; no file/capture is produced.
            before = pipe.Requests().Count(request => request.GetProperty("cmd").GetString() == "save_replay");
            save.PerformClick();
            Until(() => pipe.Requests().Count(request => request.GetProperty("cmd").GetString() == "save_replay") == before + 1 && !vm.IsReplaySavePending);
            replay.PerformClick();
            Until(() => !client.IsRunning && vm.CaptureStatus == "Off" && !vm.IsCaptureActive && vm.CanControlReplay); StopTimers(vm);
            Assert.False(window.IsVisible);
            Invoke(window, "UpdateStatusBadgeColor");
            Assert.Equal("Start replay", replay.Text);
            replay.PerformClick();
            Until(() => vm.CaptureStatus == "Buffering" && vm.CaptureHost.CurrentProfile != null && vm.RecordingHealth.Profile != null);
            StopTimers(vm);
            Hotkey(keys, 19001);
            Until(() => !client.IsRunning && vm.CaptureStatus == "Off" && !vm.IsCaptureActive && vm.CanControlReplay); StopTimers(vm);
            Assert.False(window.IsVisible);

            // Shutdown/update gates share the production tray/global route.
            foreach (string gate in new[] { "_quitInProgress", "_updateInstallInProgress", "_isRealClosing" })
            {
                Set(window, gate, true); before = pipe.Requests().Length;
                Hotkey(keys, 19001); Hotkey(keys, 19002);
                Invoke(window, "ShowAndRestoreWindow"); Drain();
                Assert.Equal(before, pipe.Requests().Length);
                Assert.False(window.IsVisible);
                Set(window, gate, false);
            }
            Assert.False(vm.DiagnosticsInitialized);
        }
        finally
        {
            try
            {
                // Synthetic IDs were never native registrations.
                if (keys != null) { Set(keys, "_toggleId", 0); Set(keys, "_saveId", 0); keys.Dispose(); }
                if (vm != null) { StopTimers(vm); vm.Dispose(); }
                original?.Dispose();
                if (window != null)
                {
                    ((IDisposable)Field(window, "_notificationService")).Dispose();
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }

    private static void Hotkey(HotkeyManager manager, int id)
    {
        object[] args = [IntPtr.Zero, 0x0312, new IntPtr(id), IntPtr.Zero, false];
        typeof(HotkeyManager).GetMethod("HwndHook", Hidden)!.Invoke(manager, args);
        Assert.True((bool)args[4]);
    }
    private static object Field(object value, string name) => value.GetType().GetField(name, Hidden)!.GetValue(value)!;
    private static void Set(object value, string name, object? data) => value.GetType().GetField(name, Hidden)!.SetValue(value, data);
    private static object? Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Hidden)!.Invoke(value, args);
    private static void StopTimers(MainViewModel vm)
    {
        foreach (var field in typeof(MainViewModel).GetFields(Hidden))
            if (field.GetValue(vm) is DispatcherTimer timer) timer.Stop();
    }
    private static void Drain() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Until(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (complete() || watch.Elapsed > TimeSpan.FromSeconds(12)) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.True(complete(), "Hidden command route did not finish within its bound.");
    }
}
