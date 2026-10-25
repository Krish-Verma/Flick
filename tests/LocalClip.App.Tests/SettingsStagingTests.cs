using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LocalClip.App;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Audio;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

public partial class SettingsStagingTests
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
                void DrainBindings()
                {
                    // WPF attaches bindings at Loaded and marshals worker
                    // notifications at ContextIdle. A DataBind-only sentinel
                    // starves those real queues in this unshown-window fixture.
                    for (int pass = 0; pass < 3; pass++)
                        app.Dispatcher.Invoke(() => { },
                            System.Windows.Threading.DispatcherPriority.ContextIdle,
                            CancellationToken.None, TimeSpan.FromSeconds(2));
                }
                // Testhost owns Application.ResourceAssembly. Load the actual
                // production dictionaries by assembly-qualified pack URI.
                foreach (string dictionary in new[] { "Colors", "Metrics", "Typography", "Icons", "Controls", "Chrome" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                        Source = new Uri($"/Flick;component/Resources/Theme/Flick.{dictionary}.xaml", UriKind.Relative)
                    });
                ResourceUsageSettingsAssertions.Run();
                SettingsPersistenceAssertions.Run();
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
                // Audio choices are staged, preserve exact process provenance,
                // and never enable a microphone or commit live preferences.
                var audioPreferences = new AppSettings { AudioMode = "Separated",
                    GameAudioSource = new LocalClip.Core.Audio.AudioProcessIdentity(42, 123, @"C:\Synthetic\game.exe", "Generated game") };
                typeof(SettingsWindow).GetMethod("PopulateAudio", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, new object[] { audioPreferences });
                Assert.Equal("SystemFallback", settings.AudioMode);
                var audioStaged = new AppSettings();
                typeof(SettingsWindow).GetMethod("StageAudio", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, new object[] { audioStaged });
                Assert.Equal("Separated", audioStaged.AudioMode);
                Assert.Equal(audioPreferences.GameAudioSource, audioStaged.GameAudioSource);
                Assert.False(audioStaged.EnableMicrophone);
                Assert.Null(settings.GameAudioSource);
                typeof(SettingsWindow).GetMethod("PopulateAudio", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, new object[] { settings });

                // The actual WPF playback/microphone selectors stage exact
                // Windows endpoint IDs without changing user settings or
                // turning on a microphone before an explicit saved opt-in.
                var outputPicker = Find<ComboBox>("CmbSystemAudioDevice");
                var inputPicker = Find<ComboBox>("CmbMicrophoneDevice");
                Assert.Equal("default", Assert.IsType<AudioEndpointChoice>(outputPicker.SelectedItem).Id);
                Assert.Equal("default", Assert.IsType<AudioEndpointChoice>(inputPicker.SelectedItem).Id);
                var outputChoices = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Output, "test-output",
                    new[] { new AudioEndpoint("test-output", "Test USB headphones", false) });
                var inputChoices = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Input, "test-input",
                    new[] { new AudioEndpoint("test-input", "Test USB microphone", false) });
                outputPicker.ItemsSource = outputChoices;
                inputPicker.ItemsSource = inputChoices;
                outputPicker.SelectedItem = outputChoices.First(x => x.Id == "test-output");
                inputPicker.SelectedItem = inputChoices.First(x => x.Id == "test-input");
                Find<CheckBox>("ChkMicrophone").IsChecked = true;
                var endpointsStaged = AppSettings.CreateDefault();
                typeof(SettingsWindow).GetMethod("StageAudio", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(page, new object[] { endpointsStaged });
                Assert.Equal("test-output", endpointsStaged.SystemAudioDeviceId);
                Assert.Equal("test-input", endpointsStaged.MicrophoneDeviceId);
                Assert.Equal("default", settings.SystemAudioDeviceId);
                Assert.Equal("default", settings.MicrophoneDeviceId);
                Assert.False(settings.EnableMicrophone);
                Find<CheckBox>("ChkMicrophone").IsChecked = false;
                typeof(SettingsWindow).GetMethod("PopulateAudio", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(page, new object[] { settings });
                // Exercise the actual disclosure template: its keyboard-focusable
                // toggle must update the Expander and reveal/hide its content.
                var disclosure = Find<Expander>("WebhookExpander");
                disclosure.ApplyTemplate();
                var header = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(
                    disclosure.Template.FindName("HeaderSite", disclosure));
                var disclosureContent = Assert.IsType<ContentPresenter>(
                    disclosure.Template.FindName("ExpandSite", disclosure));
                header.IsChecked = true;
                DrainBindings();
                Assert.True(disclosure.IsExpanded);
                Assert.Equal(Visibility.Visible, disclosureContent.Visibility);
                header.IsChecked = false;
                DrainBindings();
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
                    // Keep synthetic observations independent of the real
                    // no-host polling timer while dispatcher layout work runs.
                    Assert.IsType<System.Windows.Threading.DispatcherTimer>(typeof(MainViewModel)
                        .GetField("_pollTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)).Stop();
                    Assert.False(vm.DiagnosticsInitialized); // Unshown windows must not open a journal.
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
                        DrainBindings();
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

                    // A misrouted/repeated replay-toggle WM_HOTKEY while a
                    // genuine clip is still pending must not turn recording Off.
                    // Exercise the actual ViewModel route without creating a host.
                    var saveRequestField = typeof(MainViewModel).GetField(
                        "_saveRequestOperations", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    saveRequestField.SetValue(vm, 1);
                    try
                    {
                        vm.ToggleCaptureAsync(DiagnosticControlOrigin.GlobalHotkey).GetAwaiter().GetResult();
                        Assert.Equal("Armed", vm.CaptureStatus);
                        Assert.Contains("still saving", vm.StatusDetails, StringComparison.OrdinalIgnoreCase);
                        reported = null;
                        vm.SaveReplayAsync(DiagnosticControlOrigin.GlobalHotkey).GetAwaiter().GetResult();
                        Assert.Null(reported); // Never show a second false failure toast.
                    }
                    finally { saveRequestField.SetValue(vm, 0); }

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

                    // A2: opening/refreshing the disclosure must not initialize
                    // persistent diagnostics in an unshown fixture window.
                    vm.RefreshDiagnosticsStatusAsync().GetAwaiter().GetResult();
                    Assert.False(vm.DiagnosticsInitialized);
                    Assert.Null(typeof(MainViewModel).GetField("_diagnosticJournal", hidden)!.GetValue(vm));
                    Assert.Null(typeof(MainViewModel).GetField("_diagnosticInitialization", hidden)!.GetValue(vm));
                    var exportDiagnostics = Assert.IsType<Button>(window.FindName("BtnExportDiagnostics"));
                    var closeHealth = Assert.IsType<Button>(window.FindName("BtnCloseRecordingHealth"));
                    var diagnosticStatus = Assert.IsType<TextBlock>(window.FindName("TxtDiagnosticStatus"));
                    var privacy = Assert.IsType<TextBlock>(window.FindName("TxtDiagnosticPrivacy"));
                    var healthHeader = Assert.IsType<Grid>(window.FindName("RecordingHealthHeader"));
                    var diagnosticFooter = Assert.IsType<StackPanel>(window.FindName("RecordingDiagnosticsFooter"));
                    var healthScroll = Assert.IsType<ScrollViewer>(window.FindName("RecordingHealthScroll"));
                    Assert.True(exportDiagnostics.Focusable);
                    Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(exportDiagnostics)));
                    Assert.Equal(TextWrapping.Wrap, privacy.TextWrapping);
                    Assert.Equal(System.Windows.Automation.AutomationLiveSetting.Polite,
                        System.Windows.Automation.AutomationProperties.GetLiveSetting(diagnosticStatus));
                    var busyField = typeof(MainViewModel).GetField("_isExportingDiagnostics", hidden)!;
                    var diagnosticStatusField = typeof(MainViewModel).GetField("_diagnosticStatusMessage", hidden)!;
                    void PublishDiagnostics()
                    {
                        foreach (string property in new[] { nameof(vm.DiagnosticStatusText), nameof(vm.CanExportDiagnostics) })
                            typeof(MainViewModel).GetMethod("OnPropertyChanged", hidden)!.Invoke(vm, new object[] { property });
                        DrainBindings();
                    }
                    long healthRevisionBeforeExportState = health.Snapshot.Revision;
                    busyField.SetValue(vm, true);
                    PublishDiagnostics();
                    Assert.False(exportDiagnostics.IsEnabled);
                    Assert.True(closeHealth.IsEnabled);
                    Assert.Equal(healthRevisionBeforeExportState, health.Snapshot.Revision);
                    busyField.SetValue(vm, false);
                    diagnosticStatusField.SetValue(vm,
                        "Synthetic fixture: local diagnostics are unavailable. Recording can continue. Choose another export location and try again.");
                    PublishDiagnostics();
                    Assert.Equal(vm.CanExportDiagnostics, exportDiagnostics.IsEnabled);
                    Assert.Equal(vm.DiagnosticStatusText, diagnosticStatus.Text);
                    Assert.False(vm.DiagnosticsInitialized);
                    Assert.True(health.RecordRecordingIssue(session, "fixture-long-error",
                        "Synthetic capture failure: the selected source became unavailable. Choose another source and restart replay. " +
                        "This retained message deliberately exercises scrolling health details at the minimum window size. " +
                        "The fixed Close and Export Diagnostics controls must remain accessible while these details scroll."));
                    vm.CaptureStatus = "Faulted";
                    PublishHealth();
                    typeof(MainWindow).GetField("_layoutMode", hidden)!.SetValue(window, LocalClip.Core.Rules.WorkspaceLayoutMode.Compact);
                    typeof(MainWindow).GetMethod("ShowCompactEditor", hidden)!.Invoke(window, null);
                    var client = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                    Rect ClientBounds(FrameworkElement element) => element.TransformToAncestor(client)
                        .TransformBounds(new Rect(element.RenderSize));
                    void AssertContained(Rect outer, Rect inner)
                    {
                        outer.Inflate(0.5, 0.5); // Allow half a physical pixel for layout rounding at96DPI.
                        Assert.True(outer.Contains(inner), $"Expected {inner} to fit within {outer}.");
                    }
                    foreach (var size in new[] { new Size(720, 480), new Size(800, 600) })
                    {
                        healthScroll.ScrollToTop();
                        for (int pass = 0; pass < 3; pass++)
                        {
                            DrainBindings();
                            InvalidateVisualMeasure(client);
                            client.Measure(size);
                            client.Arrange(new Rect(size));
                            client.UpdateLayout();
                        }
                        Rect panelBounds = ClientBounds(healthPanel);
                        Rect headerBounds = ClientBounds(healthHeader), footerBounds = ClientBounds(diagnosticFooter);
                        Rect scrollBounds = ClientBounds(healthScroll);
                        AssertContained(new Rect(size), panelBounds);
                        AssertContained(panelBounds, headerBounds);
                        AssertContained(panelBounds, footerBounds);
                        AssertContained(headerBounds, ClientBounds(closeHealth));
                        AssertContained(footerBounds, ClientBounds(exportDiagnostics));
                        Assert.True(headerBounds.Bottom <= scrollBounds.Top + 0.5);
                        Assert.True(scrollBounds.Bottom <= footerBounds.Top + 0.5);
                        Assert.True(double.IsFinite(healthScroll.ViewportHeight) && healthScroll.ViewportHeight > 0);
                        Assert.True(healthScroll.ScrollableHeight > 0); // Exercise the fixed footer under real overflow.
                        healthScroll.ScrollToBottom();
                        client.UpdateLayout();
                        Assert.True(healthScroll.VerticalOffset > 0);
                        Assert.Equal(headerBounds, ClientBounds(healthHeader));
                        Assert.Equal(footerBounds, ClientBounds(diagnosticFooter));
                    }

                    // A3: build immutable consent without opening a helper,
                    // changing preferences, or enabling the configured mic.
                    var runRecordingTest = Assert.IsType<Button>(window.FindName("BtnRunRecordingDiagnostic"));
                    var includeTestAudio = Assert.IsType<CheckBox>(window.FindName("ChkDiagnosticAudio"));
                    Assert.False(includeTestAudio.IsChecked);
                    syntheticTarget.MonitorDevice = "SYNTHETIC_DISPLAY_ID";
                    vm.CaptureStatus = "Off";
                    bool microphoneBeforePlan = vm.Settings.EnableMicrophone;
                    vm.Settings.EnableMicrophone = true;
                    var silentPlan = Assert.IsType<RecordingDiagnosticPlan>(vm.CreateRecordingDiagnosticPlan(false));
                    Assert.False(silentPlan.EnableSystemAudio);
                    Assert.False(silentPlan.EnableMicrophone);
                    Assert.Equal(syntheticTarget.DisplayName, silentPlan.Target.Name);
                    Assert.Contains("Computer audio: Off", silentPlan.ConsentSummary);
                    Assert.Contains("Microphone: Off", silentPlan.ConsentSummary);
                    var audioPlan = Assert.IsType<RecordingDiagnosticPlan>(vm.CreateRecordingDiagnosticPlan(true));
                    Assert.True(audioPlan.EnableSystemAudio);
                    Assert.True(audioPlan.EnableMicrophone);
                    Assert.True(vm.Settings.EnableMicrophone); // Consent planning never commits an override.
                    vm.Settings.EnableMicrophone = microphoneBeforePlan;
                    object? epochBeforeDecline = epochField.GetValue(vm);
                    Guid healthSessionBeforeDecline = vm.RecordingHealth.SessionId;
                    vm.RunRecordingDiagnosticAsync(silentPlan, Guid.Empty).GetAwaiter().GetResult();
                    Assert.Contains("changed", vm.DiagnosticTestStatusText);
                    Assert.False(vm.IsRecordingDiagnosticRunning);
                    Assert.Equal(epochBeforeDecline, epochField.GetValue(vm));
                    Assert.False(host.IsRunning);
                    foreach (string activeState in new[] { "Starting", "Buffering", "Armed", "Stopping" })
                    {
                        vm.CaptureStatus = activeState;
                        DrainBindings();
                        Assert.False(vm.CanRunRecordingDiagnostic);
                        Assert.False(runRecordingTest.IsEnabled);
                        Assert.Null(vm.CreateRecordingDiagnosticPlan(false));
                    }
                    vm.CaptureStatus = "Off";
                    var saveOperationsField = typeof(MainViewModel).GetField("_saveRequestOperations", hidden)!;
                    saveOperationsField.SetValue(vm, 1);
                    try
                    {
                        Assert.False(vm.CanRunRecordingDiagnostic);
                        Assert.Null(vm.CreateRecordingDiagnosticPlan(false));
                    }
                    finally { saveOperationsField.SetValue(vm, 0); }
                    var runningTestField = typeof(MainViewModel).GetField("_isRecordingDiagnosticRunning", hidden)!;
                    var cancellingTestField = typeof(MainViewModel).GetField("_recordingDiagnosticCancelling", hidden)!;
                    void PublishRecordingTest()
                    {
                        typeof(MainViewModel).GetMethod("PublishDiagnosticTest", hidden)!.Invoke(vm, null);
                        DrainBindings();
                    }
                    runningTestField.SetValue(vm, true);
                    PublishRecordingTest();
                    Assert.Equal("Cancel Test", runRecordingTest.Content);
                    Assert.True(runRecordingTest.IsEnabled); // Running must not disable its own Cancel action.
                    Assert.False(includeTestAudio.IsEnabled);
                    vm.CancelRecordingDiagnostic();
                    DrainBindings();
                    Assert.True(vm.IsRecordingDiagnosticRunning); // Cleanup has not completed.
                    Assert.False(runRecordingTest.IsEnabled);
                    Assert.Equal("Finishing Test…", runRecordingTest.Content);
                    runningTestField.SetValue(vm, false);
                    cancellingTestField.SetValue(vm, false);
                    PublishRecordingTest();

                    // Exercise the actual same-window consent and decline No.
                    // The queued observer runs inside FlickDialog's dispatcher
                    // frame; neither the fixture nor the modal ever clicks Yes.
                    app.MainWindow = window;
                    Exception? declineFailure = null;
                    bool observedConsent = false;
                    string ordinarySyntheticName = syntheticTarget.DisplayName;
                    syntheticTarget.DisplayName = "Synthetic overflow source: " +
                        string.Join(" ", Enumerable.Repeat("exact retained recording test source", 96));
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var modalHost = Assert.IsType<ContentControl>(window.FindName("DialogContentHost"));
                        InvalidateVisualMeasure(client);
                        client.Measure(new Size(720, 480));
                        client.Arrange(new Rect(0, 0, 720, 480));
                        client.UpdateLayout();
                        var no = FindVisualDescendant<Button>(modalHost);
                        try
                        {
                            Assert.True(FlickDialog.IsOpen);
                            Assert.NotNull(no);
                            Assert.Equal("No", no.Content);
                            observedConsent = true;
                            Assert.False(host.IsRunning);
                            Assert.False(vm.IsRecordingDiagnosticRunning);
                            Assert.False(vm.DiagnosticsInitialized);
                            AssertContained(new Rect(0, 0, 720, 480), ClientBounds(no));
                            var yes = Assert.Single(Assert.IsAssignableFrom<Panel>(no.Parent).Children.OfType<Button>(),
                                button => button.Content?.ToString() == "Yes");
                            AssertContained(new Rect(0, 0, 720, 480), ClientBounds(yes));
                            var consentBody = FindVisualDescendant<ScrollViewer>(modalHost);
                            Assert.NotNull(consentBody);
                            Assert.True(double.IsFinite(consentBody.ViewportHeight) && consentBody.ViewportHeight > 0);
                            Assert.True(consentBody.ScrollableHeight > 0);
                            Rect noBeforeScroll = ClientBounds(no), yesBeforeScroll = ClientBounds(yes);
                            consentBody.ScrollToBottom();
                            client.UpdateLayout();
                            page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                            client.UpdateLayout();
                            Assert.True(consentBody.VerticalOffset > 0);
                            Assert.Equal(noBeforeScroll, ClientBounds(no));
                            Assert.Equal(yesBeforeScroll, ClientBounds(yes));
                        }
                        catch (Exception error) { declineFailure = error; }
                        finally
                        {
                            if (no?.Content?.ToString() == "No")
                                no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                    }), System.Windows.Threading.DispatcherPriority.Normal);
                    try
                    {
                        typeof(MainWindow).GetMethod("BtnRunRecordingDiagnostic_Click", hidden)!
                            .Invoke(window, new object[] { runRecordingTest, new RoutedEventArgs() });
                    }
                    finally { syntheticTarget.DisplayName = ordinarySyntheticName; }
                    if (declineFailure != null) ExceptionDispatchInfo.Capture(declineFailure).Throw();
                    Assert.True(observedConsent);
                    Assert.False(FlickDialog.IsOpen);
                    Assert.False(vm.IsRecordingDiagnosticRunning);
                    Assert.False(vm.DiagnosticsInitialized);
                    Assert.Equal(epochBeforeDecline, epochField.GetValue(vm));
                    Assert.Equal(healthSessionBeforeDecline, vm.RecordingHealth.SessionId);
                    Assert.Null(typeof(MainViewModel).GetField("_recordingDiagnosticCancellation", hidden)!.GetValue(vm));

                    // Turning the microphone off during a private recording
                    // test must cancel and await that helper's confirmed exit.
                    // Exercise the actual privacy-stop method without saving
                    // Settings, touching a journal, or invoking native capture.
                    var testCancellationField = typeof(MainViewModel).GetField("_recordingDiagnosticCancellation", hidden)!;
                    var testCompletionField = typeof(MainViewModel).GetField("_recordingDiagnosticCompletion", hidden)!;
                    var cleanupUnconfirmedField = typeof(MainViewModel).GetField("_diagnosticCleanupUnconfirmed", hidden)!;
                    using var testCancellation = new CancellationTokenSource();
                    var testCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    testCancellationField.SetValue(vm, testCancellation);
                    testCompletionField.SetValue(vm, testCompletion);
                    runningTestField.SetValue(vm, true);
                    cancellingTestField.SetValue(vm, false);
                    PublishRecordingTest();
                    try
                    {
                        typeof(MainWindow).GetMethod("OpenSettings", hidden)!.Invoke(window, null);
                        var activeSettings = Assert.IsType<SettingsWindow>(typeof(MainWindow).GetField("_settingsPage", hidden)!.GetValue(window));
                        var captureActive = Assert.IsType<Func<bool>>(typeof(SettingsWindow).GetField("_isCaptureActive", hidden)!.GetValue(activeSettings));
                        Assert.True(captureActive()); // The real Settings callback recognizes the private test.
                        typeof(MainWindow).GetMethod("CloseSettingsPage", hidden)!.Invoke(window, new object[] { false });
                        var stopMicrophone = typeof(MainWindow).GetMethod("StopCaptureForMicPrivacyAsync", hidden)!;
                        var stoppingMicrophone = Assert.IsAssignableFrom<Task>(stopMicrophone.Invoke(window, null));
                        Assert.True(testCancellation.IsCancellationRequested);
                        Assert.False(stoppingMicrophone.IsCompleted);
                        Assert.Contains("Disabling the microphone", vm.StatusDetails);
                        Assert.DoesNotContain("test stopped", vm.StatusDetails);
                        runningTestField.SetValue(vm, false);
                        cancellingTestField.SetValue(vm, false);
                        testCompletion.TrySetResult();
                        var privacyFrame = new System.Windows.Threading.DispatcherFrame();
                        var privacyGuard = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                        privacyGuard.Tick += (_, _) => privacyFrame.Continue = false;
                        _ = stoppingMicrophone.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => privacyFrame.Continue = false)),
                            TaskScheduler.Default);
                        privacyGuard.Start();
                        try { System.Windows.Threading.Dispatcher.PushFrame(privacyFrame); }
                        finally { privacyGuard.Stop(); }
                        Assert.True(stoppingMicrophone.IsCompletedSuccessfully, "Microphone release did not await test cleanup.");
                        Assert.Contains("Microphone disabled and recording test stopped", vm.StatusDetails);
                        Assert.False(host.IsRunning);
                        Assert.False(vm.DiagnosticsInitialized);
                        cleanupUnconfirmedField.SetValue(vm, true);
                        Assert.IsAssignableFrom<Task>(stopMicrophone.Invoke(window, null)).GetAwaiter().GetResult();
                        Assert.Contains("exit was not confirmed", vm.StatusDetails);
                        Assert.DoesNotContain("recording test stopped", vm.StatusDetails);
                    }
                    finally
                    {
                        testCompletion.TrySetResult();
                        testCancellationField.SetValue(vm, null);
                        testCompletionField.SetValue(vm, null);
                        runningTestField.SetValue(vm, false);
                        cancellingTestField.SetValue(vm, false);
                        cleanupUnconfirmedField.SetValue(vm, false);
                        PublishRecordingTest();
                    }

                    VerifyGameSettingsAndReservations(vm, window);

                    // Quit must cancel an active diagnostics export and wait
                    // for its cleanup, without beginning capture teardown first.
                    // A completed stop fixture prevents any native IPC here.
                    var exportCancellationField = typeof(MainViewModel).GetField("_diagnosticExportCancellation", hidden)!;
                    var exportCompletionField = typeof(MainViewModel).GetField("_diagnosticExportCompletion", hidden)!;
                    using var exportCancellation = new CancellationTokenSource();
                    var exportCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    exportCancellationField.SetValue(vm, exportCancellation);
                    exportCompletionField.SetValue(vm, exportCompletion);
                    busyField.SetValue(vm, true);
                    stopTaskField.SetValue(vm, Task.CompletedTask);
                    object? epochBeforeQuit = epochField.GetValue(vm);
                    string stateBeforeQuit = vm.CaptureStatus;
                    try
                    {
                        Task<bool> quit = vm.PrepareToQuitAsync();
                        Assert.True(exportCancellation.IsCancellationRequested);
                        Assert.False(quit.IsCompleted); // Cancellation alone is not cleanup completion.
                        Assert.Equal(stateBeforeQuit, vm.CaptureStatus);
                        Assert.Equal(epochBeforeQuit, epochField.GetValue(vm));
                        exportCompletion.TrySetResult();
                        var frame = new System.Windows.Threading.DispatcherFrame();
                        var guard = new System.Windows.Threading.DispatcherTimer {
                            Interval = TimeSpan.FromSeconds(2)
                        };
                        guard.Tick += (_, _) => frame.Continue = false;
                        _ = quit.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                            TaskScheduler.Default);
                        guard.Start();
                        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
                        finally { guard.Stop(); }
                        Assert.True(quit.IsCompletedSuccessfully, "Quit did not resume after diagnostics cleanup completed.");
                        Assert.True(quit.GetAwaiter().GetResult());
                        Assert.Equal(stateBeforeQuit, vm.CaptureStatus);
                        Assert.False(host.IsRunning);
                        Assert.False(vm.DiagnosticsInitialized);
                    }
                    finally
                    {
                        exportCompletion.TrySetResult();
                        exportCancellationField.SetValue(vm, null);
                        exportCompletionField.SetValue(vm, null);
                        busyField.SetValue(vm, false);
                        stopTaskField.SetValue(vm, null);
                        typeof(MainViewModel).GetField("_isPreparingToQuit", hidden)!.SetValue(vm, false);
                    }
                    // Health is available above Settings too. Escape must close
                    // it before the normal page/editor keyboard guard returns.
                    var workspacePage = Assert.IsType<Border>(window.FindName("WorkspacePageLayer"));
                    workspacePage.Visibility = Visibility.Visible;
                    using var keyboardSource = new HwndSource(new HwndSourceParameters("Flick health keyboard fixture") {
                        Width = 1, Height = 1, WindowStyle = 0
                    }); // Hidden input source, never attached to or showing MainWindow.
                    var escape = new KeyEventArgs(Keyboard.PrimaryDevice, keyboardSource, Environment.TickCount, Key.Escape) {
                        RoutedEvent = Keyboard.PreviewKeyDownEvent
                    };
                    typeof(MainWindow).GetMethod("MainWindow_PreviewKeyDown", hidden)!
                        .Invoke(window, new object[] { window, escape });
                    Assert.True(escape.Handled);
                    Assert.Equal(Visibility.Collapsed, healthPanel.Visibility);
                    workspacePage.Visibility = Visibility.Collapsed;
                    OrganizationEditorAssertions.Run(vm);
                    ThumbnailOperationAssertions.Run(vm);
                    SharePresentationAssertions.AssertStaleNotificationsAndSamePathRepair(window, vm);
                    ResourceSamplingAssertions.RunLastAndDispose(vm);
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

    private static void InvalidateVisualMeasure(DependencyObject visual)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            InvalidateVisualMeasure(VisualTreeHelper.GetChild(visual, i));
        if (visual is UIElement element) element.InvalidateMeasure();
    }

    private static T? FindVisualDescendant<T>(DependencyObject visual) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            var child = VisualTreeHelper.GetChild(visual, i);
            if (child is T match) return match;
            if (FindVisualDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
