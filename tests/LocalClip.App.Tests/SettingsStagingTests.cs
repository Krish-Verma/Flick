using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LocalClip.App;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

public class SettingsStagingTests
{
    [Fact]
    public void ActualWpfSettingsCancelDoesNotCommitShortcutOrSoundChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Testhost owns Application.ResourceAssembly. Load the actual
                // production dictionaries by assembly-qualified pack URI.
                foreach (string dictionary in new[] { "Colors", "Metrics", "Typography", "Icons", "Controls", "Chrome" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                        Source = new Uri($"/Flick;component/Resources/Theme/Flick.{dictionary}.xaml", UriKind.Relative)
                    });
                var settings = AppSettings.CreateDefault();
                var page = new SettingsWindow(settings, () => false);
                T Find<T>(string name) => (T)page.FindName(name);
                void Invoke(string method, object sender) => typeof(SettingsWindow)
                    .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(page, new[] { sender, new RoutedEventArgs() });
                Assert.Equal(Visibility.Collapsed, Find<Button>("BtnInstallUpdate").Visibility);
                Assert.True(Find<Button>("BtnCheckUpdates").IsEnabled);
                Assert.True(Find<CheckBox>("ChkPreviewUpdates").IsChecked);
                Assert.True(Find<CheckBox>("ChkSoftwareFallback").IsChecked);
                Assert.True(settings.AllowSoftwareFallback);
                // Exercise the actual disclosure template: its keyboard-focusable
                // toggle must update the Expander and reveal/hide its content.
                var disclosure = Find<Expander>("WebhookExpander");
                disclosure.ApplyTemplate();
                var header = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(
                    disclosure.Template.FindName("HeaderSite", disclosure));
                var disclosureContent = Assert.IsType<ContentPresenter>(
                    disclosure.Template.FindName("ExpandSite", disclosure));
                header.IsChecked = true;
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.True(disclosure.IsExpanded);
                Assert.Equal(Visibility.Visible, disclosureContent.Visibility);
                header.IsChecked = false;
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                Assert.False(disclosure.IsExpanded);
                Assert.Equal(Visibility.Collapsed, disclosureContent.Visibility);
                var toggle = Find<TextBox>("TxtToggleHotkey");
                var save = Find<TextBox>("TxtSaveHotkey");
                Assert.True(toggle.IsReadOnly);
                Assert.True(save.IsReadOnly);
                bool editing = false;
                page.ShortcutEditingChanged += value => editing = value;
                Invoke("ChangeShortcut_Click", new Button { Tag = "Toggle" });
                Assert.True(editing);
                Assert.Equal(Visibility.Visible, Find<StackPanel>("ShortcutEditor").Visibility);
                Invoke("Save_Click", page); // Incomplete shortcut must not write settings/startup.
                Assert.True(editing);
                Assert.Equal(Visibility.Visible, Find<StackPanel>("ShortcutEditor").Visibility);
                Invoke("CancelShortcut_Click", page);
                Assert.False(editing);
                Assert.Equal("Ctrl+Shift+F9", toggle.Text);
                Assert.Equal("Ctrl+Shift+F9", settings.CaptureToggleHotkey);
                Find<ComboBox>("CmbNotificationTone").SelectedIndex = 1;
                Find<CheckBox>("ChkSoftwareFallback").IsChecked = false;
                bool closed = false;
                page.CloseRequested += saved => { Assert.False(saved); closed = true; };
                Invoke("Cancel_Click", page);
                Assert.True(closed);
                Assert.Equal("Chime", settings.ClipSavedSoundTone);
                Assert.True(settings.AllowSoftwareFallback);
                // No overlay/sound requested, even with a missing configured file.
                using var notification = new FlickNotificationService(page.Dispatcher);
                notification.NotifySaved(30, false, false, "Custom", @"Z:\missing.wav");
                Assert.False(FlickDialog.IsOpen);

                // Exercise the real capture controls in the same WPF Application.
                // WPF permits only one Application instance per AppDomain; avoid
                // a second test creating its own competing Application singleton.
                var window = new MainWindow(); // Never shown; no native recording started.
                var vm = Assert.IsType<MainViewModel>(window.DataContext);
                try
                {
                    vm.Settings.ShowClipSavedOverlay = false; // No test desktop toast.
                    var saveButton = Assert.IsType<Button>(window.FindName("BtnSaveReplay"));
                    var updateCaptureUi = typeof(MainWindow).GetMethod(
                        "UpdateStatusBadgeColor", BindingFlags.NonPublic | BindingFlags.Instance)!;

                    vm.CaptureStatus = "Buffering";
                    updateCaptureUi.Invoke(window, null);
                    Assert.False(saveButton.IsEnabled);
                    vm.CaptureStatus = "Armed";
                    updateCaptureUi.Invoke(window, null);
                    Assert.True(saveButton.IsEnabled);
                    vm.CaptureStatus = "Off";
                    updateCaptureUi.Invoke(window, null);
                    Assert.False(saveButton.IsEnabled);

                    string? reported = null;
                    vm.ReplaySaveFailed += reason => reported = reason;
                    vm.SaveReplayAsync().GetAwaiter().GetResult();
                    Assert.NotNull(reported);
                    Assert.Contains("Replay is off", reported);
                    Assert.Contains(vm.Settings.CaptureToggleHotkey, reported);

                    // A1: simulate observations through the production health
                    // ledger, not through a capture device or a fake media file.
                    const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
                    var health = Assert.IsType<RecordingHealthLedger>(typeof(MainViewModel)
                        .GetField("_health", hidden)!.GetValue(vm));
                    Guid session = health.BeginSession(new RecordingSource("Monitor", "Synthetic test display"));
                    typeof(MainViewModel).GetField("_healthSession", hidden)!.SetValue(vm, session);
                    void PublishHealth()
                    {
                        typeof(MainViewModel).GetMethod("PublishHealth", hidden)!.Invoke(vm, null);
                        page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                        updateCaptureUi.Invoke(window, null);
                    }
                    vm.CaptureStatus = "Armed";
                    var profile = new CaptureEncodingProfile("obs_qsv11_v2", "Intel Quick Sync H.264", false, 1920, 1080, 60);
                    typeof(MainViewModel).GetMethod("SetCaptureProfile", hidden)!.Invoke(vm, new object[] { profile });
                    health.ObserveHost(session, CaptureHostHealth.Responsive);
                    health.ObserveBuffer(session, 29.5, 30, estimated: false);
                    vm.BufferedDurationText = "29.5s / 30s";
                    Assert.True(health.BeginSave(session, "synthetic-save"));
                    Assert.True(health.MarkSaveValidating(session, "synthetic-save"));
                    PublishHealth();
                    Assert.Equal(RecordingEngineState.Ready, vm.RecordingHealth.EngineState);
                    Assert.Equal(ReplaySaveState.Validating, vm.RecordingHealth.SaveState);
                    Assert.True(vm.IsArmed); // Saving does not pretend the engine stopped.
                    var healthStatus = Assert.IsType<TextBlock>(window.FindName("TxtHealthStatus"));
                    Assert.Equal("Saving Clip", healthStatus.Text);

                    Assert.True(health.RecordValidatedClip(session, "synthetic-save", "synthetic-summary", 12.5));
                    PublishHealth();
                    Assert.Equal("Save Successful", healthStatus.Text);
                    Assert.Contains("12.5", vm.HealthLastSavedText);
                    health.RecordRecordingIssue(session, "synthetic-fault", "Synthetic capture failure retained for review.");
                    vm.CaptureStatus = "Faulted";
                    PublishHealth();
                    Assert.False(saveButton.IsEnabled);
                    Assert.Equal("Recording Faulted", healthStatus.Text);
                    Assert.Contains("Synthetic capture failure", vm.HealthLastFailureText);
                    Assert.DoesNotContain("29.5s / 30s", vm.HealthDetailsText);

                    vm.CaptureStatus = "Off";
                    health.ObserveEngine(session, RecordingEngineState.Stopped);
                    health.ObserveHost(session, CaptureHostHealth.Exited);
                    vm.ReportHotkeyHealth("Save replay", "Ctrl+Shift+F8", null, "Synthetic shortcut conflict");
                    PublishHealth();
                    Assert.Contains("Synthetic capture failure", vm.HealthLastFailureText);
                    Assert.Contains("12.5", vm.HealthLastSavedText);
                    Assert.Contains("Unavailable", vm.HealthHotkeysText);

                    // A late native Off notification must not end managed
                    // shutdown early and make the capture controls reusable.
                    var hostStateChanged = typeof(MainViewModel).GetMethod("OnHostStateChanged", hidden)!;
                    vm.CaptureStatus = "Stopping";
                    hostStateChanged.Invoke(vm, new object[] { "Off" });
                    updateCaptureUi.Invoke(window, null);
                    Assert.Equal("Stopping", vm.CaptureStatus);
                    Assert.False(vm.IsArmed);
                    Assert.False(saveButton.IsEnabled);

                    // Queued active observations cannot resurrect a stopped or
                    // faulted session, even when they carry its matching token.
                    foreach (string terminalState in new[] { "Off", "Faulted" })
                    {
                        vm.CaptureStatus = terminalState;
                        hostStateChanged.Invoke(vm, new object[] { "Armed" });
                        updateCaptureUi.Invoke(window, null);
                        Assert.Equal(terminalState, vm.CaptureStatus);
                        Assert.False(vm.IsArmed);
                        Assert.False(saveButton.IsEnabled);
                    }

                    // A valid selected source makes this exercise the stop-task
                    // guard rather than the independent missing-source guard.
                    // Set the fixture directly to avoid persisting source settings.
                    vm.CaptureStatus = "Off";
                    var syntheticTarget = new TargetItem { Type = "Monitor", DisplayName = "Synthetic test display" };
                    vm.Targets.Add(syntheticTarget);
                    typeof(MainViewModel).GetField("_selectedTarget", hidden)!.SetValue(vm, syntheticTarget);
                    var stopTaskField = typeof(MainViewModel).GetField("_stopCaptureTask", hidden)!;
                    var epochField = typeof(MainViewModel).GetField("_captureEpoch", hidden)!;
                    var sessionField = typeof(MainViewModel).GetField("_healthSession", hidden)!;
                    var pendingStop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var host = Assert.IsType<CaptureHostClient>(typeof(MainViewModel).GetField("_captureHost", hidden)!.GetValue(vm));
                    var hostPathField = typeof(CaptureHostClient).GetField("_hostExePath", hidden)!;
                    var originalHostPath = hostPathField.GetValue(host);
                    // Even a broken guard must never launch a recorder in this test.
                    hostPathField.SetValue(host, System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        Guid.NewGuid().ToString("N"), "missing-capture-helper.exe"));
                    object? epochBeforeStart = epochField.GetValue(vm);
                    object? sessionBeforeStart = sessionField.GetValue(vm);
                    long revisionBeforeStart = vm.RecordingHealth.Revision;
                    stopTaskField.SetValue(vm, pendingStop.Task);
                    try
                    {
                        vm.StartCaptureAsync().GetAwaiter().GetResult();
                        Assert.Equal("Off", vm.CaptureStatus);
                        Assert.Equal(epochBeforeStart, epochField.GetValue(vm));
                        Assert.Equal(sessionBeforeStart, sessionField.GetValue(vm));
                        Assert.Equal(revisionBeforeStart, vm.RecordingHealth.Revision);
                        Assert.False(host.IsRunning);
                    }
                    finally
                    {
                        stopTaskField.SetValue(vm, null);
                        hostPathField.SetValue(host, originalHostPath);
                        pendingStop.TrySetResult(true);
                    }

                    var healthButton = Assert.IsType<Button>(window.FindName("BtnRecordingHealth"));
                    var healthPanel = Assert.IsType<Border>(window.FindName("RecordingHealthPanel"));
                    healthButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(Visibility.Visible, healthPanel.Visibility);
                    Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(healthPanel));
                    // Health is available above Settings too. Escape must close
                    // it before the normal page/editor keyboard guard returns.
                    var workspacePage = Assert.IsType<Border>(window.FindName("WorkspacePageLayer"));
                    workspacePage.Visibility = Visibility.Visible;
                    using var keyboardSource = new HwndSource(new HwndSourceParameters("Flick health keyboard fixture") {
                        Width = 1, Height = 1, WindowStyle = 0
                    }); // Hidden input source, never attached to or showing MainWindow.
                    var escape = new KeyEventArgs(Keyboard.PrimaryDevice, keyboardSource, Environment.TickCount, Key.Escape);
                    typeof(MainWindow).GetMethod("MainWindow_PreviewKeyDown", hidden)!
                        .Invoke(window, new object[] { window, escape });
                    Assert.True(escape.Handled);
                    Assert.Equal(Visibility.Collapsed, healthPanel.Visibility);
                    workspacePage.Visibility = Visibility.Collapsed;
                }
                finally { vm.Dispose(); }
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF staging test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
