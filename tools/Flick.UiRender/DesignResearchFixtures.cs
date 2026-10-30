using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LocalClip.App;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.App.Views.Components;
using LocalClip.Core.Audio;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Storage;

namespace Flick.UiRender;

internal static partial class Program
{
    private static IReadOnlyList<string> RenderDesignResearchFixtures(MainWindow window, MainViewModel vm,
        AppSettings settings, ClipStorage storage, string fixtures, string output, Action<string, int, int> render)
    {
        T Main<T>(string name) => (T)window.FindName(name);
        void WindowCall(string method, params object?[] args) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, args);
        void VmCall(string method, params object?[] args) => typeof(MainViewModel).GetMethod(method, Private)!.Invoke(vm, args);
        void Notify(string name) => VmCall("OnPropertyChanged", name);
        var failures = new List<string>();
        var observations = new List<object>();
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        string profile = AppSettings.GetSettingsFilePath();
        byte[] originalProfile = SHA256.HashData(File.ReadAllBytes(profile));
        string originalPreferences = JsonSerializer.Serialize(vm.Settings);
        string index = (string)typeof(ClipStorage).GetField("_metadataFile", Private)!.GetValue(storage)!;
        byte[]? originalIndex = File.Exists(index) ? SHA256.HashData(File.ReadAllBytes(index)) : null;
        var originalSelected = vm.SelectedClip;
        string originalSearch = vm.SearchQuery, originalSource = vm.ClipSourceFilter,
            originalGame = vm.GameFilter, originalPeriod = vm.LibraryPeriod, originalSort = vm.LibrarySort;
        bool originalFavorites = vm.FavoritesOnly;
        string originalTags = vm.TagsDraft, originalGameDraft = vm.GameDraft, originalEditState = vm.EditorSaveState;
        var originalConfirmDiscard = vm.ConfirmDiscardEditorChanges;
        var originalUndo = ((List<(double Start, double End)>)typeof(MainViewModel).GetField("_trimUndo", Private)!.GetValue(vm)!).ToArray();
        var vmState = new[] { "_health", "_healthSession", "_captureStatus", "_captureProfile", "_isArmed", "_bufferedDurationText", "_statusDetails", "_storage" }
            .ToDictionary(name => name, name => typeof(MainViewModel).GetField(name, Private)!.GetValue(vm));
        var mode = typeof(MainWindow).GetField("_layoutMode", Private)!;
        var compactEditor = typeof(MainWindow).GetField("_compactShowingEditor", Private)!;
        object? originalMode = mode.GetValue(window), originalCompact = compactEditor.GetValue(window);
        var originalTool = (Button?)typeof(MainWindow).GetField("_activeEditorTool", Private)!.GetValue(window);
        Visibility originalHealthVisibility = Main<FrameworkElement>("RecordingHealthPanel").Visibility;
        var root = (FrameworkElement)window.Content;
        Size originalSize = root.RenderSize;
        var timeline = Main<TrimRangeControl>("TrimTimeline");
        var originalRange = (timeline.OriginalDuration, timeline.SelectionStart, timeline.SelectionEnd);
        SettingsWindow? page = null;
        int prohibitedCalls = 0, endpointStubCalls = 0, startupStatusStubCalls = 0;
        bool restored = false;
        // Earlier fixture groups release their owned pages. Refuse to discard
        // any unexpectedly retained page before entering this group's cleanup.
        if (typeof(MainWindow).GetField("_settingsPage", Private)!.GetValue(window) != null ||
            typeof(MainWindow).GetField("_foldersPage", Private)!.GetValue(window) != null)
            throw new InvalidOperationException("Design fixtures require the prior fixture page to be released.");
        try
        {
            if (vm.CaptureHost.IsRunning || vm.DiagnosticsInitialized)
                throw new InvalidOperationException("Design fixtures require an unstarted capture host and diagnostics journal.");
            vm.ConfirmDiscardEditorChanges = () => true;
            WindowCall("ReturnToClipWorkspace");
            WindowCall("CloseEditorContext");
            Main<FrameworkElement>("RecordingHealthPanel").Visibility = Visibility.Collapsed;
            mode.SetValue(window, WorkspaceLayoutMode.Split);
            WindowCall("ApplyWorkspaceLayout");
            var ledger = new RecordingHealthLedger();
            typeof(MainViewModel).GetField("_health", Private)!.SetValue(vm, ledger);
            Guid session = ledger.BeginSession(new RecordingSource("Monitor", "Synthetic report health source · no recording"));
            typeof(MainViewModel).GetField("_healthSession", Private)!.SetValue(vm, session);
            vm.CaptureStatus = "Armed";
            ledger.ObserveHost(session, CaptureHostHealth.Responsive);
            ledger.ObserveBuffer(session, 30, 30, estimated: false);
            vm.BufferedDurationText = "30s / 30s";
            vm.StatusDetails = "Synthetic health observations · no capture or media validation performed";
            const string saveId = "design-report-simulated-save";
            Check(ledger.BeginSave(session, saveId), "The synthetic ledger did not accept a pending save request.");
            Health("151-report-save-requested-wide.png", "Save requested…", ReplaySaveState.Requested, 1280, 720);
            Check(ledger.AcknowledgeSave(session, saveId, true), "The synthetic save ACK was not correlated.");
            Health("152-report-save-accepted-minimum.png", "Saving clip…", ReplaySaveState.Accepted, 720, 480);
            Check(ledger.MarkSaveAcknowledgementUnconfirmed(session, saveId), "Unconfirmed ACK state could not be observed.");
            Health("153-report-save-unconfirmed-minimum.png", "Save still being checked", ReplaySaveState.Accepted, 720, 480);
            Check(ledger.MarkSaveValidating(session, saveId), "The correlated save did not enter validation.");
            vm.CaptureStatus = "Off";
            ledger.ObserveHost(session, CaptureHostHealth.Exited);
            Health("154-report-checking-after-stop-minimum.png", "Checking saved video…", ReplaySaveState.Validating, 720, 480);
            Check(ledger.Snapshot.PendingSaveCount == 1 && ledger.Snapshot.EngineState == RecordingEngineState.Off &&
                !ledger.Snapshot.SaveAcknowledgementUnconfirmed, "Stopping replay settled a pending validation or retained the obsolete ACK warning.");
            Check(ledger.RecordValidatedClip(session, saveId, "simulated-summary-only-no-media-claim", 18.5),
                "The simulated validated result was not correlated with its pending save.");
            Main<FrameworkElement>("RecordingHealthPanel").Visibility = Visibility.Visible;
            Health("155-report-simulated-success-after-stop-wide.png", "Clip saved", ReplaySaveState.Succeeded, 1280, 720);
            Check(ledger.Snapshot.PendingSaveCount == 0 && ledger.Snapshot.LastValidatedClip?.DurationSeconds == 18.5,
                "Simulated validated success did not retain its actual supplied duration.");
            Main<FrameworkElement>("RecordingHealthPanel").Visibility = Visibility.Collapsed;

            // Drive the real empty presentation and real reset buttons. The first
            // two states retain a no-match query while filters are cleared.
            vm.ClearLibrarySearch(); vm.ClearLibraryFilters();
            vm.FavoritesOnly = true;
            vm.GameFilter = "Synthetic report game with no matches";
            vm.SearchQuery = "synthetic-report-query-with-no-matches";
            Library("156-report-no-matching-clips-minimum.png", false);
            var empty = Main<StackPanel>("LibraryEmptyState");
            var reset = ProductVisuals(empty).OfType<Button>().First(button => Equals(button.Content, "Reset filters"));
            Check(IsInRenderedPath(reset, root) && reset.IsEnabled, "No-match state omitted its actionable filter reset.");
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Library("157-report-filters-reset-search-kept-minimum.png", false);
            Check(!vm.LibraryHasFilters && vm.LibraryHasSearch && vm.FilteredClips.Count == 0,
                "Reset filters erased the search or did not clear the filters.");
            Main<Button>("BtnClearClipSearch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Library("158-report-search-cleared-minimum.png", false);
            Check(!vm.LibraryHasSearch && vm.FilteredClips.Count == storage.GetAllClips().Count && vm.FilteredClips.Count > 0,
                "The real Clear search action did not restore the original clip results.");

            var emptyPreferences = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            var emptyStorage = new ClipStorage(emptyPreferences, metadataDirectory: Path.Combine(fixtures, "design-report-empty-index"));
            typeof(MainViewModel).GetField("_storage", Private)!.SetValue(vm, emptyStorage);
            vm.ReloadClips();
            Library("159-report-empty-library-minimum.png", true);
            Check(vm.SelectedClip == null && emptyStorage.GetAllClips().Count == 0,
                "The empty-library fixture retained a selected clip or nonempty index.");
            var choose = ProductVisuals(empty).OfType<Button>().First(button => Equals(button.Content, "Choose source"));
            Check(IsInRenderedPath(choose, root) && choose.IsEnabled &&
                ProductContains(RelativeBounds(empty, root), RelativeBounds(choose, root)),
                "The true empty library omitted a contained actionable Choose source control.");
            typeof(MainViewModel).GetField("_storage", Private)!.SetValue(vm, storage);
            vm.ReloadClips();
            vm.SelectedClip = originalSelected ?? vm.FilteredClips.FirstOrDefault();

            page = MakeSettings("draft");
            ShowSettings(page);
            var duration = (TextBox)page.FindName("TxtDuration");
            string originalDuration = duration.Text;
            duration.Text = "60";
            if (duration.Text == originalDuration) duration.Text = "90";
            Settings("160-report-settings-unsaved-minimum.png", true, true);
            duration.Text = originalDuration;
            Settings("161-report-settings-reverted-minimum.png", false, false);
            duration.Text = "invalid";
            Settings("162-report-settings-inline-error-minimum.png", true, false);
            var error = (TextBlock)page.FindName("TxtDurationError");
            Rect fieldBounds = RelativeBounds(duration, page), errorBounds = RelativeBounds(error, page);
            Check(IsInRenderedPath(error, page) && error.Text.Contains("15–120") && errorBounds.Top >= fieldBounds.Bottom &&
                errorBounds.Top - fieldBounds.Bottom <= 36, "Invalid duration omitted adjacent recovery text.");
            page.LeavePage();
            page = MakeSettings("startup-repair", startup: true);
            ShowSettings(page);
            typeof(SettingsWindow).GetMethod("SelectCategory", Private)!.Invoke(page, ["General"]);
            Settings("163-report-settings-startup-repair-minimum.png", false, true);
            Check(((TextBlock)page.FindName("TxtStartupStatus")).Text.Contains("repair", StringComparison.OrdinalIgnoreCase),
                "An unchanged enabled startup preference omitted its repair explanation.");
            page.LeavePage();
            page = MakeSettings("audio");
            ShowSettings(page);
            typeof(SettingsWindow).GetMethod("SelectCategory", Private)!.Invoke(page, ["Audio"]);
            var outputs = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Output, "synthetic-report-headset",
                [new AudioEndpoint("synthetic-report-headset", "Synthetic USB headset · game playback", false)]);
            var inputs = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Input, "synthetic-report-mic",
                [new AudioEndpoint("synthetic-report-mic", "Synthetic USB microphone · commentary", false)]);
            var outputPicker = (ComboBox)page.FindName("CmbSystemAudioDevice");
            var inputPicker = (ComboBox)page.FindName("CmbMicrophoneDevice");
            outputPicker.ItemsSource = outputs; outputPicker.SelectedItem = outputs.First(choice => choice.Id == "synthetic-report-headset");
            inputPicker.ItemsSource = inputs; inputPicker.SelectedItem = inputs.First(choice => choice.Id == "synthetic-report-mic");
            render("164-report-settings-audio-roles-wide.png", 1280, 720);
            Check(((AudioEndpointChoice)outputPicker.SelectedItem).Label.Contains("Synthetic USB headset") &&
                ((AudioEndpointChoice)inputPicker.SelectedItem).Label.Contains("Synthetic USB microphone") &&
                ((CheckBox)page.FindName("ChkMicrophone")).IsChecked == false && !inputPicker.IsEnabled,
                "Audio roles lost the supplied device labels or enabled microphone consent.");
            observations.Add(new { Image = "164-report-settings-audio-roles-wide.png", Kind = "Synthetic endpoint labels in actual controls",
                Output = ((AudioEndpointChoice)outputPicker.SelectedItem).Label, Microphone = ((AudioEndpointChoice)inputPicker.SelectedItem).Label,
                MicrophoneOptIn = ((CheckBox)page.FindName("ChkMicrophone")).IsChecked, InputEnabled = inputPicker.IsEnabled });
            WindowCall("ReturnToClipWorkspace");
            page.LeavePage();

            // Only SetRange is used: no commit event, metadata edit or source
            // decoder. Logical focus is reported separately from native focus.
            mode.SetValue(window, WorkspaceLayoutMode.Compact);
            WindowCall("ShowCompactEditor");
            WindowCall("EditorTool_Click", Main<Button>("ToolTrim"), new RoutedEventArgs());
            timeline.SetRange(3600, 1800, 1800.25);
            Trim("165-report-trim-in-narrow-minimum.png", "StartHandle");
            Trim("166-report-trim-out-narrow-minimum.png", "EndHandle");
            WindowCall("CloseEditorContext");
            timeline.SetRange(originalRange.OriginalDuration, originalRange.SelectionStart, originalRange.SelectionEnd);
            WindowCall("ResetPlayerForSelection");
            WindowCall("RestorePosterAfterShow");
            render("167-report-selected-poster-minimum.png", 720, 480);
            var play = Main<Button>("BtnPlayPoster");
            Check(vm.SelectedClip != null && Main<MediaElement>("VideoPlayer").Source == null &&
                IsInRenderedPath(play, root) && play.IsEnabled, "A selected clip lacked a usable poster Play action or opened media before Play.");
            observations.Add(new { Image = "167-report-selected-poster-minimum.png", Kind = "Actual selected-clip poster controls",
                HasSelection = vm.SelectedClip != null, MediaSourceOpened = Main<MediaElement>("VideoPlayer").Source != null,
                PlayVisible = IsInRenderedPath(play, root), PlayEnabled = play.IsEnabled });
        }
        finally
        {
            WindowCall("ReturnToClipWorkspace");
            page?.LeavePage();
            WindowCall("CloseEditorContext");
            foreach (var value in vmState) typeof(MainViewModel).GetField(value.Key, Private)!.SetValue(vm, value.Value);
            vm.ClearLibrarySearch(); vm.ClearLibraryFilters();
            vm.ReloadClips();
            vm.SelectedClip = originalSelected;
            vm.SearchQuery = originalSearch; vm.ClipSourceFilter = originalSource; vm.FavoritesOnly = originalFavorites;
            vm.GameFilter = originalGame; vm.LibraryPeriod = originalPeriod; vm.LibrarySort = originalSort;
            vm.TagsDraft = originalTags; vm.GameDraft = originalGameDraft;
            typeof(MainViewModel).GetField("_editorSaveState", Private)!.SetValue(vm, originalEditState);
            var undo = (List<(double Start, double End)>)typeof(MainViewModel).GetField("_trimUndo", Private)!.GetValue(vm)!;
            undo.Clear(); undo.AddRange(originalUndo);
            vm.ConfirmDiscardEditorChanges = originalConfirmDiscard;
            foreach (string name in new[] { "CaptureStatus", "IsArmed", "BufferedDurationText", "StatusDetails", "CaptureEncodingText", "SoftwareFallbackWarning", "EditorSaveState", "CanUndoTrim" }) Notify(name);
            VmCall("PublishHealth");
            mode.SetValue(window, originalMode); compactEditor.SetValue(window, originalCompact);
            WindowCall("ApplyWorkspaceLayout");
            Main<FrameworkElement>("RecordingHealthPanel").Visibility = originalHealthVisibility;
            if (originalTool != null) WindowCall("EditorTool_Click", originalTool, new RoutedEventArgs());
            timeline.SetRange(originalRange.OriginalDuration, originalRange.SelectionStart, originalRange.SelectionEnd);
            WindowCall("UpdateStatusBadgeColor");
            if (originalSize.Width > 0 && originalSize.Height > 0)
            { root.Measure(originalSize); root.Arrange(new Rect(originalSize)); root.UpdateLayout(); }
            DrainDispatcherThroughContextIdle(window.Dispatcher);
            bool profileUnchanged = originalProfile.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profile)));
            bool indexUnchanged = originalIndex == null ? !File.Exists(index) : File.Exists(index) && originalIndex.SequenceEqual(SHA256.HashData(File.ReadAllBytes(index)));
            restored = profileUnchanged && indexUnchanged && originalPreferences == JsonSerializer.Serialize(vm.Settings) &&
                ReferenceEquals(vmState["_health"], typeof(MainViewModel).GetField("_health", Private)!.GetValue(vm)) &&
                !vm.CaptureHost.IsRunning && !vm.DiagnosticsInitialized && prohibitedCalls == 0;
            File.WriteAllText(Path.Combine(output, "design-research-fixture-evidence.json"), JsonSerializer.Serialize(new {
                ActualWpfControls = true, HealthUsesProductionInMemoryLedger = true,
                HealthSuccessIsSimulatedObservationOnly = true, NoMediaValidationOrSavedClipClaim = true,
                NativeFocusAcceptanceNotClaimed = true, OriginalProfileUnchanged = profileUnchanged,
                OriginalLibraryIndexUnchanged = indexUnchanged, OriginalStateRestored = restored,
                EndpointDiscoveryUsesSyntheticStubOnly = true, EndpointStubCalls = endpointStubCalls,
                StartupStatusUsesSyntheticStubOnly = true, StartupStatusStubCalls = startupStatusStubCalls,
                ProhibitedLiveCalls = prohibitedCalls, CapturePerformed = false, ClipboardUsed = false,
                SaveInvoked = false, Observations = observations, Failures = failures
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!restored) throw new InvalidOperationException("Design research fixtures changed retained state or started a live subsystem.");
        }
        return failures;

        void Health(string image, string expected, ReplaySaveState state, int width, int height)
        {
            mode.SetValue(window, width < 1008 ? WorkspaceLayoutMode.Compact : WorkspaceLayoutMode.Split);
            if (width < 1008) WindowCall("ShowCompactLibrary"); else WindowCall("ApplyWorkspaceLayout");
            VmCall("PublishHealth"); WindowCall("UpdateStatusBadgeColor");
            render(image, width, height);
            var actual = Main<TextBlock>("TxtCaptureStatus");
            Check(actual.Text == expected && vm.RecordingHealth.SaveState == state, image + ": capture bar disagrees with correlated health state.");
            Check(IsInRenderedPath(actual, root) && ProductContains(new Rect(root.RenderSize), RelativeBounds(actual, root)),
                image + ": capture status is outside its rendered viewport.");
            observations.Add(new { Image = image, Kind = "Synthetic health observations through production ledger and capture bar",
                Actual = actual.Text, Expected = expected, Engine = vm.RecordingHealth.EngineState.ToString(),
                Save = vm.RecordingHealth.SaveState.ToString(), vm.RecordingHealth.PendingSaveCount,
                vm.RecordingHealth.SaveAcknowledgementUnconfirmed, Detail = vm.CaptureStateDetail,
                SimulatedValidationOnly = true, StatusBounds = ResearchBounds(RelativeBounds(actual, root)) });
        }

        void Library(string image, bool trulyEmpty)
        {
            mode.SetValue(window, WorkspaceLayoutMode.Compact); WindowCall("ShowCompactLibrary");
            render(image, 720, 480);
            var empty = Main<StackPanel>("LibraryEmptyState");
            bool expectedEmpty = vm.FilteredClips.Count == 0;
            Check(IsInRenderedPath(empty, root) == expectedEmpty, image + ": bound empty state does not match result count.");
            if (expectedEmpty)
                Check(ProductVisuals(empty).OfType<TextBlock>().Any(text => text.Text == (trulyEmpty ? "No clips yet" : "No matching clips")),
                    image + ": empty and no-match states are not distinguished.");
            observations.Add(new { Image = image, Kind = trulyEmpty ? "Isolated empty ClipStorage" : "Actual search/filter bindings",
                vm.LibraryHasClips, vm.LibraryHasSearch, vm.LibraryHasFilters, vm.ActiveLibraryFilterCount,
                MatchingClips = vm.FilteredClips.Count, EmptyVisible = IsInRenderedPath(empty, root),
                Title = vm.LibraryEmptyTitle, Hint = vm.LibraryEmptyHint,
                ClearButtonVisible = IsInRenderedPath(Main<Button>("BtnClearClipSearch"), root) });
        }

        SettingsWindow MakeSettings(string name, bool startup = false)
        {
            var clone = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            string directory = Path.Combine(fixtures, "design-report-settings", name);
            clone.StorageRoot = directory; clone.OriginalsFolder = Path.Combine(directory, "Originals");
            clone.ExportsFolder = Path.Combine(directory, "Exports"); clone.TempFolder = Path.Combine(directory, "Temp");
            clone.StartWithWindows = startup; clone.GameDetectionEnabled = false; clone.ApprovedGameProfiles = [];
            clone.AudioMode = "SystemFallback"; clone.GameAudioSource = clone.VoiceAudioSource = null;
            clone.EnableMicrophone = false; clone.SystemAudioDeviceId = clone.MicrophoneDeviceId = "default";
            clone.EnableDiscordWebhook = false; clone.MaskedWebhookUrl = "";
            return new SettingsWindow(clone, () => false,
                () => { prohibitedCalls++; throw new InvalidOperationException("No app discovery in design fixtures."); },
                flow => {
                    endpointStubCalls++;
                    if (name != "audio") return Array.Empty<AudioEndpoint>();
                    return flow == AudioEndpointFlow.Output
                        ? new[] { new AudioEndpoint("synthetic-report-headset", "Synthetic USB headset · game playback", false) }
                        : new[] { new AudioEndpoint("synthetic-report-mic", "Synthetic USB microphone · commentary", false) };
                },
                _ => { prohibitedCalls++; throw new InvalidOperationException("No Settings Save in design fixtures."); },
                (_, _) => { prohibitedCalls++; throw new InvalidOperationException("No startup writes in design fixtures."); },
                _ => { startupStatusStubCalls++; return new(WindowsStartupRegistrationState.MissingOrStale, "Save Settings to repair Windows startup."); });
        }

        void ShowSettings(SettingsWindow target)
        {
            WindowCall("ReturnToClipWorkspace");
            mode.SetValue(window, WorkspaceLayoutMode.Compact);
            WindowCall("ApplyWorkspaceLayout");
            typeof(MainWindow).GetField("_settingsPage", Private)!.SetValue(window, target);
            WindowCall("ShowWorkspacePage", target);
        }

        void Settings(string image, bool dirty, bool saveable)
        {
            render(image, 720, 480);
            var current = page!;
            var save = (Button)current.FindName("BtnSaveSettings");
            var cancel = (Button)current.FindName("BtnCancelSettings");
            var scroll = (ScrollViewer)current.FindName("SettingsScroll");
            Rect viewport = new(current.RenderSize), saveBounds = RelativeBounds(save, current), cancelBounds = RelativeBounds(cancel, current);
            bool fixedActions = ProductContains(viewport, saveBounds) && ProductContains(viewport, cancelBounds) &&
                !HasVisualAncestor(save, scroll, current) && !HasVisualAncestor(cancel, scroll, current) &&
                RelativeBounds(scroll, current).Bottom <= saveBounds.Top + 1;
            Check(current.HasUnsavedChanges == dirty && current.CanSaveDraft == saveable && save.IsEnabled == saveable,
                image + ": draft/revert/repair eligibility is inconsistent.");
            Check(fixedActions && cancel.IsEnabled, image + ": Settings fixed actions are not contained or available.");
            observations.Add(new { Image = image, Kind = "Cloned Settings staged controls; no Save invoked",
                current.HasUnsavedChanges, current.CanSaveDraft, SaveEnabled = save.IsEnabled,
                Status = ((TextBlock)current.FindName("TxtSaveStatus")).Text,
                FixedActions = fixedActions, SaveBounds = ResearchBounds(saveBounds), CancelBounds = ResearchBounds(cancelBounds) });
        }

        void Trim(string image, string handleName)
        {
            var handle = (Thumb)timeline.FindName(handleName);
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(handle), handle);
            handle.Focus();
            render(image, 720, 480);
            var canvas = (Canvas)timeline.FindName("RangeCanvas");
            var start = (Thumb)timeline.FindName("StartHandle"); var end = (Thumb)timeline.FindName("EndHandle");
            Rect startBounds = RelativeBounds(start, canvas), endBounds = RelativeBounds(end, canvas);
            bool contained = ProductContains(new Rect(canvas.RenderSize), startBounds) && ProductContains(new Rect(canvas.RenderSize), endBounds);
            bool distinctTargets = startBounds.Right <= endBounds.Left + .001;
            bool hit = ResearchTrimHit(canvas, start) && ResearchTrimHit(canvas, end);
            bool logical = ReferenceEquals(FocusManager.GetFocusedElement(FocusManager.GetFocusScope(handle)), handle);
            Check(contained && distinctTargets && hit && start.ActualWidth >= 24 && end.ActualWidth >= 24 && logical,
                image + ": narrow trim targets overlap, escape their canvas or lose hit/logical-focus reachability.");
            Check(timeline.SelectionStart == 1800 && timeline.SelectionEnd == 1800.25,
                image + ": focus changed the exact trim selection.");
            observations.Add(new { Image = image, Kind = "Actual narrow selection with logical-focus request",
                FocusHandle = handleName, LogicalFocus = logical, KeyboardFocus = handle.IsKeyboardFocused,
                NativeKeyboardFocusClaimed = false, timeline.SelectionStart, timeline.SelectionEnd,
                TargetsContained = contained, DistinctTargets = distinctTargets, TargetsHitTested = hit,
                StartBounds = ResearchBounds(startBounds), EndBounds = ResearchBounds(endBounds) });
        }
    }

    private static object ResearchBounds(Rect bounds) => new { bounds.Left, bounds.Top, bounds.Width, bounds.Height };

    private static bool ResearchTrimHit(Canvas canvas, Thumb target)
    {
        var point = new Point(Canvas.GetLeft(target) + target.ActualWidth / 2, Canvas.GetTop(target) + target.ActualHeight / 2);
        DependencyObject? hit = VisualTreeHelper.HitTest(canvas, point)?.VisualHit;
        while (hit != null && hit is not Thumb) hit = VisualTreeHelper.GetParent(hit);
        return ReferenceEquals(hit, target);
    }
}
