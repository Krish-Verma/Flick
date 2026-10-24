using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.Audio;
using LocalClip.Core.GameDetection;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Storage;

namespace Flick.UiRender;

internal static partial class Program
{
    private static void RenderProductPhaseFixtures(MainWindow window, MainViewModel vm, AppSettings settings,
        ClipStorage storage, string fixtures, string output, Action<string, int, int> render)
    {
        T Find<T>(string name) => (T)window.FindName(name);
        void Invoke(string method, params object?[] values) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, values);
        var observations = new List<object>();
        bool integrityPassed = false;
        string profile = AppSettings.GetSettingsFilePath();
        byte[] profileBefore = File.ReadAllBytes(profile);
        Find<Border>("RecordingHealthPanel").Visibility = Visibility.Collapsed;
        Invoke("CloseEditorContext"); // Cancel any Share still-image work before changing panes.
        Invoke("ReturnToClipWorkspace");
        vm.CaptureStatus = "Off";
        typeof(MainViewModel).GetMethod("SetCaptureProfile", Private)!.Invoke(vm, [null]);
        Invoke("UpdateStatusBadgeColor");

        // A detached production SettingsWindow stages these cloned preferences.
        // No discovery/approval/refresh/Save action is invoked. These names,
        // executable paths, PIDs and birth values represent no real processes.
        var staged = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        staged.GameDetectionEnabled = false;
        staged.ApprovedGameProfiles = [
            new() { Id = Guid.NewGuid(), Name = "Orbit Arena (synthetic)", ExecutablePath = @"C:\Synthetic Games\Orbit Arena\orbit.exe",
                Enabled = true, QualityPreset = "Smooth", ReplayDurationSeconds = 60 },
            new() { Id = Guid.NewGuid(), Name = "Rally Circuit (synthetic)", ExecutablePath = @"C:\Synthetic Games\Rally Circuit\rally.exe",
                Enabled = false, QualityPreset = "Light", ReplayDurationSeconds = 30 }
        ];
        staged.AudioMode = "Separated";
        staged.EnableMicrophone = false;
        staged.GameAudioSource = new AudioProcessIdentity(42420, 132000000000000001, @"C:\Synthetic Games\Orbit Arena\orbit.exe", "Orbit Arena (synthetic)");
        staged.VoiceAudioSource = new AudioProcessIdentity(42421, 132000000000000002, @"C:\Synthetic Apps\Voice Room\voice.exe", "Voice Room (synthetic)");
        var page = new SettingsWindow(staged, () => false,
            () => throw new InvalidOperationException("A rendering fixture must never discover running apps."));
        typeof(MainWindow).GetField("_settingsPage", Private)!.SetValue(window, page);
        Invoke("ShowWorkspacePage", page);
        T Page<T>(string name) => (T)page.FindName(name);
        void Category(string name) => typeof(SettingsWindow).GetMethod("SelectCategory", Private)!.Invoke(page, [name]);
        var scroll = Page<ScrollViewer>("SettingsScroll");
        void SettingsFrame(string file, int width, int height, string category)
        {
            render(file, width, height);
            var save = Page<Button>("BtnSaveSettings");
            var cancel = Page<Button>("BtnCancelSettings");
            var bounds = new Rect(page.RenderSize);
            Rect Bounds(FrameworkElement control) => control.TransformToAncestor(page).TransformBounds(new Rect(control.RenderSize));
            bool visibleActions = ProductContains(bounds, Bounds(save)) && ProductContains(bounds, Bounds(cancel));
            bool finiteScroll = scroll.ViewportHeight > 0 && double.IsFinite(scroll.ViewportHeight);
            observations.Add(new { Image = file, Category = category, Width = width, Height = height,
                SaveAndCancelInsideSettings = visibleActions, FinitePositiveScrollViewport = finiteScroll,
                scroll.ViewportHeight, scroll.ScrollableHeight, scroll.VerticalOffset,
                MicrophoneChecked = Page<CheckBox>("ChkMicrophone").IsChecked,
                StagedOnly = true });
            WriteEvidence();
            if (!visibleActions || !finiteScroll) throw new InvalidOperationException("Settings fixed actions or scroll viewport do not fit in " + file);
        }
        Category("Games");
        Page<CheckBox>("ChkGameDetectionEnabled").IsChecked = true;
        Page<ListBox>("LstApprovedGames").SelectedIndex = 0;
        Page<TextBlock>("TxtGameMessage").Text = "Synthetic staged preferences only · no window scan, approval or recording performed.";
        SettingsFrame("49-games-staged-720.png", 1280, 720, "Games overview");
        scroll.ScrollToBottom();
        SettingsFrame("50-games-profile-720.png", 1280, 720, "Selected game profile");
        scroll.ScrollToTop();
        SettingsFrame("51-games-staged-minimum.png", 720, 480, "Games overview");
        scroll.ScrollToBottom();
        SettingsFrame("52-games-profile-minimum.png", 720, 480, "Selected game profile");

        Category("Audio");
        Page<TextBlock>("TxtAudioSources").Text = "Synthetic staged app identities · no process enumeration, endpoint access or audio capture performed.";
        SettingsFrame("53-separate-app-audio-720.png", 1280, 720, "Separate App Audio");
        scroll.ScrollToTop();
        SettingsFrame("54-separate-app-audio-minimum.png", 720, 480, "Separate App Audio");
        scroll.ScrollToBottom();
        SettingsFrame("55-separate-app-audio-microphone-minimum.png", 720, 480, "Independent microphone choice");
        if (settings.GameDetectionEnabled || typeof(MainViewModel).GetField("_gameTimer", Private)!.GetValue(vm) != null)
            throw new InvalidOperationException("Staged Games rendering must not enable live detection.");
        // At minimum height every category remains reachable by the real
        // ScrollViewer BringIntoView path; no control coordinates are changed.
        var navigation = Page<ScrollViewer>("SettingsNavScroll");
        var navigationChecks = new List<object>();
        foreach (string name in new[] { "NavRecording", "NavGames", "NavAudio", "NavShortcuts", "NavStorage", "NavGeneral", "NavSharing" })
        {
            var button = Page<Button>(name);
            button.BringIntoView();
            for (int pass = 0; pass < 3; pass++)
            {
                DrainDispatcherThroughContextIdle(page.Dispatcher);
                page.UpdateLayout();
            }
            Rect location = button.TransformToAncestor(navigation).TransformBounds(new Rect(button.RenderSize));
            bool reachable = ProductContains(new Rect(navigation.RenderSize), location);
            navigationChecks.Add(new { Name = name, Reachable = reachable, navigation.VerticalOffset,
                navigation.ViewportHeight, navigation.ScrollableHeight });
            if (!reachable || !double.IsFinite(navigation.ViewportHeight) || navigation.ViewportHeight <= 0)
                throw new InvalidOperationException("Settings category is unreachable at minimum height: " + name);
        }
        Category("Sharing");
        SettingsFrame("67-settings-navigation-minimum.png", 720, 480, "Last category reached through actual navigation scrolling");
        observations.Add(new { SettingsCategoryNavigation = navigationChecks });
        Invoke("ReturnToClipWorkspace"); // Discard the actual staged page without Save.

        // Reuse the actual generated/decoded media and matching MP4 from the
        // Share fixture. Recent history metadata is synthetic; Ready is not.
        var clips = storage.GetAllClips();
        var media = clips.Single(clip => Path.GetFileName(clip.OriginalFilePath) == "Synthetic Share fixture.mkv");
        media.Title = "Overtime finish · synthetic fixture";
        media.GameName = "Orbit Arena";
        media.Tags = ["clutch", "overtime"];
        media.IsFavorite = true;
        media.Markers = [new(.5, "Opening"), new(1.75, "Finish"), new(2.5, "Replay")];
        // Share visual failures alter VM presentation only. Restore the
        // original validated export flag, then independently check its bytes.
        media.IsExportReady = true;
        media.LastExportTargetMb = vm.TargetSizeMb;
        media.LastExportedAtUtc = DateTimeOffset.UtcNow;
        if (!ReadyShareFile.TryGet(media, media.LastExportTargetMb, out string ready, settings.ExportsFolder))
            throw new InvalidOperationException("Recent-export fixture requires the actual matching Share MP4.");
        string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(media.OriginalFilePath)));
        storage.AddOrUpdateClip(media);
        int index = 0;
        foreach (var clip in clips.Where(clip => clip.Id != media.Id))
        {
            clip.GameName = index++ % 2 == 0 ? "Rally Circuit" : "Orbit Arena";
            clip.Tags = ["practice"];
            clip.IsFavorite = index == 1;
            storage.AddOrUpdateClip(clip);
        }
        vm.ReloadClips();
        vm.FavoritesOnly = false;
        vm.GameFilter = "All Games";
        vm.LibraryPeriod = "Any time";
        vm.SearchQuery = "";
        vm.LibrarySort = "Game";
        vm.SelectedClip = vm.FilteredClips.Single(clip => clip.Id == media.Id);
        Invoke("ResetPlayerForSelection");
        // Never imply that the synthetic poster is decoded gameplay/playback.
        Find<Image>("PlayerPoster").Visibility = Visibility.Collapsed;
        Find<TextBlock>("PlayerPosterHint").Text = "Synthetic media fixture · press Play to Preview";
        void Split()
        {
            typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(window, WorkspaceLayoutMode.Split);
            Invoke("ApplyWorkspaceLayout");
        }
        void Compact(bool library)
        {
            typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(window, WorkspaceLayoutMode.Compact);
            Invoke(library ? "ShowCompactLibrary" : "ShowCompactEditor");
        }
        void ProductFrame(string file, int width, int height, string state)
        {
            render(file, width, height);
            bool contextFits = true;
            bool renderedShare = false;
            if (Find<Grid>("WorkspaceContentGrid").Visibility == Visibility.Visible &&
                Find<Grid>("ClipEditorPanel").Visibility == Visibility.Visible &&
                Find<Border>("EditorContextLayer").Visibility == Visibility.Visible)
            {
                var root = (FrameworkElement)window.Content;
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                var context = Find<Border>("EditorContextLayer");
                var contextScroll = Find<ScrollViewer>("EditorContextScroll");
                FrameworkElement allocation = Grid.GetRowSpan(context) > 1
                    ? Find<FrameworkElement>("ClipEditorPanel") : Find<Border>("PreviewSurface");
                renderedShare = Find<Border>("SharePanel").Visibility == Visibility.Visible;
                contextFits = renderedShare
                    ? InspectShareNoScrollLayout(window).Fits
                    : ProductContains(Bounds(allocation), Bounds(context)) &&
                      ProductContains(Bounds(context), Bounds(Find<Button>("CloseEditorTool"))) &&
                      contextScroll.ViewportHeight > 0 && double.IsFinite(contextScroll.ViewportHeight);
            }
            bool libraryFits = true;
            double? listHeight = null;
            if (Find<Border>("ClipLibraryPanel").Visibility == Visibility.Visible)
            {
                var library = Find<Border>("ClipLibraryPanel");
                var list = Find<ListBox>("ClipList");
                listHeight = list.ActualHeight;
                Rect listBounds = list.TransformToAncestor(library).TransformBounds(new Rect(list.RenderSize));
                libraryFits = list.ActualHeight >= 96 && ProductContains(new Rect(library.RenderSize), listBounds);
            }
            observations.Add(new { Image = file, State = state, Width = width, Height = height,
                FilteredClips = vm.FilteredClips.Count, RecentExports = vm.RecentExports.Count,
                vm.FavoritesOnly, vm.GameFilter, vm.LibrarySort, Markers = vm.ClipMarkers.Count,
                vm.ExportAspectPreset, EditorContextFits = contextFits, LibraryListFits = libraryFits,
                ListHeight = listHeight, NoPlaybackOrCapture = true });
            WriteEvidence();
            // The global renderer records all Share layout failures and throws
            // after the last state. Preserve immediate failures for other tools.
            if (!contextFits && !renderedShare) throw new InvalidOperationException("Editor context, Close or scroll viewport do not fit in " + file);
            if (!libraryFits) throw new InvalidOperationException("The virtualized clip list lacks a useful contained viewport in " + file);
        }
        Split();
        ProductFrame("56-organized-library-720.png", 1280, 720, "Grouped games, favorites and clip metadata");
        Compact(library: true);
        vm.FavoritesOnly = true;
        ProductFrame("57-favorite-library-minimum.png", 720, 480, "Favorites filtered library");
        vm.FavoritesOnly = false;
        Split();
        var details = ProductVisuals(Find<FrameworkElement>("WorkspaceContentGrid")).OfType<Expander>()
            .Single(expander => Equals(expander.Header, "Clip details"));
        details.IsExpanded = true;
        vm.TagsDraft = "clutch, overtime, review";
        ProductFrame("58-clip-details-staged-720.png", 1280, 720, "Unsaved tag edit, actual save-state binding");
        Compact(library: true);
        ProductFrame("65-clip-details-minimum-top.png", 720, 480, "Expanded clip details preserve the virtualized library at minimum size");
        var detailsScroll = Find<ScrollViewer>("LibraryDetailsScroll");
        var headerScroll = Find<ScrollViewer>("LibraryHeaderScroll");
        detailsScroll.ScrollToBottom();
        var saveDetails = Find<Button>("BtnSaveClipDetails");
        saveDetails.BringIntoView();
        ProductFrame("66-clip-details-minimum-save.png", 720, 480, "Expanded details scroll to actual Save action at minimum size");
        Rect saveBounds = saveDetails.TransformToAncestor(detailsScroll).TransformBounds(new Rect(saveDetails.RenderSize));
        bool saveReachable = ProductContains(new Rect(detailsScroll.RenderSize), saveBounds);
        observations.Add(new { ExpandedLibraryDetails = true, SaveReachable = saveReachable,
            DetailsViewportHeight = detailsScroll.ViewportHeight, DetailsScrollHeight = detailsScroll.ScrollableHeight,
            HeaderViewportHeight = headerScroll.ViewportHeight, HeaderScrollHeight = headerScroll.ScrollableHeight });
        if (!saveReachable || detailsScroll.ViewportHeight <= 0 || !double.IsFinite(detailsScroll.ViewportHeight) ||
            headerScroll.ViewportHeight <= 0 || !double.IsFinite(headerScroll.ViewportHeight))
            throw new InvalidOperationException("Expanded library controls are not reachable in finite scroll viewports.");
        Split();
        vm.TagsDraft = vm.SelectedClip!.TagsText; // Discard the draft before later navigation.
        details.IsExpanded = false;
        Invoke("EditorTool_Click", Find<Button>("ToolTrim"), new RoutedEventArgs());
        ProductFrame("59-editor-markers-720.png", 1280, 720, "Trim and synthetic retained markers");
        Compact(library: false);
        ProductFrame("60-editor-markers-minimum.png", 720, 480, "Minimum editor with markers");
        Invoke("CloseEditorContext");
        vm.LibrarySort = "Recently Exported";
        if (vm.RecentExports.Count != 1 || vm.FilteredClips.Count != 1 || !vm.SelectRecentExport(media.Id))
            throw new InvalidOperationException("Recent exports must contain the genuinely ready generated fixture.");
        Split();
        Invoke("EditorTool_Click", Find<Button>("ToolShare"), new RoutedEventArgs());
        Find<ComboBox>("CmbRecentExports").SelectedIndex = 0;
        ProductFrame("61-recent-exports-720.png", 1280, 720, "Recent validated MP4 selected; history timestamp synthetic");
        Compact(library: false);
        ProductFrame("62-recent-exports-minimum.png", 720, 480, "Minimum recent exports selector and library");
        int priorCap = vm.TargetSizeMb;
        string priorTitle = media.Title;
        vm.TargetSizeMb = 250;
        Invoke("UpdateSharePresetSelection");
        media.Title = "Synthetic recent clip with a deliberately long title to verify the full tooltip and compact metadata at minimum size";
        vm.ActivateSharePresentation();
        ProductFrame("73-share-recent-custom-long-minimum.png", 720, 480, "Recent export plus real Custom250 choice and long title; no new export");
        vm.IsExporting = true;
        vm.ExportProgress = 42;
        vm.ExportStatusMessage = "Visual fixture: preparing Custom250 output; no encode worker is running.";
        Invoke("UpdateSharePresetSelection");
        ProductFrame("74-share-recent-custom-busy-minimum.png", 720, 480, "Recent export plus Custom limit and all four actions during synthetic export");
        vm.CancelShareExport();
        ProductFrame("75-share-recent-custom-cancelling-minimum.png", 720, 480, "Recent export plus Custom limit and pending synthetic cancellation");
        vm.IsExporting = false;
        vm.TargetSizeMb = priorCap;
        Invoke("UpdateSharePresetSelection");
        media.Title = priorTitle;
        vm.ActivateSharePresentation();
        Invoke("CloseEditorContext");
        vm.LibrarySort = "Game";
        Split();
        vm.ExportAspectPreset = ExportAspectPresets.Vertical9x16;
        Invoke("EditorTool_Click", Find<Button>("ToolShare"), new RoutedEventArgs());
        ProductFrame("63-export-shape-720.png", 1280, 720, "Fit 9:16 staged for next export; original picture unchanged");
        Compact(library: false);
        ProductFrame("64-export-shape-minimum.png", 720, 480, "Minimum fit 9:16 choice, not an exported portrait claim");
        Invoke("CloseEditorContext");
        if (!profileBefore.SequenceEqual(File.ReadAllBytes(profile)) || vm.CaptureHost.IsRunning || vm.DiagnosticsInitialized ||
            typeof(MainViewModel).GetField("_gameTimer", Private)!.GetValue(vm) != null ||
            originalHash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(media.OriginalFilePath))))
            throw new InvalidOperationException("Product fixtures changed preferences/original media or started a live subsystem.");
        integrityPassed = true;
        WriteEvidence();

        void WriteEvidence() => File.WriteAllText(Path.Combine(output, "product-phase-fixture-evidence.json"),
            JsonSerializer.Serialize(new {
                ActualProductionWpfViews = true,
                SettingsScope = "Cloned preferences, synthetic approved paths and app identities; no Save, scans, approval, audio endpoints or helper start",
                LibraryScope = "Existing isolated temporary storage; organization/markers and recent-history timestamps are synthetic; recent MP4 uses preceding real generated Share export",
                ShapeScope = "Actual fit 9:16 control and invalidation; no new portrait export, preview playback or capture claimed",
                WindowsOrHardwareAcceptance = false,
                OriginalAndProfileIntegrityChecksPassed = integrityPassed,
                Observations = observations
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<DependencyObject> ProductVisuals(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in ProductVisuals(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static bool ProductContains(Rect outer, Rect inner) => inner.Width > 0 && inner.Height > 0 &&
        inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1 && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1;
}
