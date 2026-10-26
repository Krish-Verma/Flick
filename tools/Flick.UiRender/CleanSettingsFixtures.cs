using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.Audio;
using LocalClip.Core.Models;

namespace Flick.UiRender;

internal static partial class Program
{
    private static void RenderCleanSettingsFixtures(MainWindow window, MainViewModel vm, AppSettings settings,
        string fixtures, string output, Action<string, int, int> render)
    {
        void Invoke(string method, params object?[] values) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, values);
        string profile = AppSettings.GetSettingsFilePath();
        byte[] profileBefore = File.ReadAllBytes(profile);
        string importStatusBefore = vm.ImportStatusText;
        var observations = new List<object>();
        bool restored = false;
        int prohibitedActions = 0;
        var staged = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        staged.GameDetectionEnabled = false;
        staged.ApprovedGameProfiles = Enumerable.Range(1, 30).Select(index => new LocalClip.Core.GameDetection.ApprovedGameProfile {
            Id = Guid.NewGuid(), Name = $"Synthetic game {index:00}",
            ExecutablePath = index == 1
                ? @"C:\Synthetic Games\An intentionally long installation folder to exercise the full executable tooltip\Another nested game folder\Synthetic game 01\game.exe"
                : $@"C:\Synthetic Games\Game{index:00}\game.exe",
            Enabled = index % 2 != 0, QualityPreset = "Balanced", ReplayDurationSeconds = 30
        }).ToList();
        staged.AudioMode = "Separated";
        staged.EnableMicrophone = false;
        staged.GameAudioSource = new AudioProcessIdentity(42420, 132000000000000001,
            @"C:\Synthetic Games\Game01\game.exe", "Synthetic game audio");
        staged.VoiceAudioSource = new AudioProcessIdentity(42421, 132000000000000002,
            @"C:\Synthetic Apps\Voice\voice.exe", "Synthetic voice audio");
        var page = new SettingsWindow(staged, () => false,
            () => { prohibitedActions++; throw new InvalidOperationException("UI fixtures cannot discover running apps."); },
            _ => { prohibitedActions++; throw new InvalidOperationException("UI fixtures cannot enumerate audio endpoints."); },
            _ => { prohibitedActions++; throw new InvalidOperationException("UI fixtures cannot save preferences."); },
            (_, _) => { prohibitedActions++; throw new InvalidOperationException("UI fixtures cannot register Windows startup."); });
        T Page<T>(string name) => (T)page.FindName(name);
        var scroll = Page<ScrollViewer>("SettingsScroll");
        void Category(string name) => typeof(SettingsWindow).GetMethod("SelectCategory", Private)!.Invoke(page, [name]);
        int image = 96;
        try
        {
            Invoke("ReturnToClipWorkspace");
            typeof(MainWindow).GetField("_settingsPage", Private)!.SetValue(window, page);
            Invoke("ShowWorkspacePage", page);
            Page<Selector>("LstApprovedGames").SelectedIndex = 0;
            foreach (string category in new[] { "Recording", "Games", "Audio", "Shortcuts", "Storage", "General", "Sharing" })
            {
                Category(category);
                if (category == "Sharing")
                {
                    Page<ComboBox>("CmbShareCap").SelectedIndex = 4;
                    Page<TextBox>("TxtShareCustomMb").Text = "250";
                    Page<Expander>("WebhookExpander").IsExpanded = true;
                }
                foreach (var size in new[] { (Width: 1280, Height: 720, Label: "wide"), (Width: 720, Height: 480, Label: "minimum") })
                {
                    scroll.ScrollToTop();
                    string top = $"{image++:00}-clean-settings-{category.ToLowerInvariant()}-{size.Label}.png";
                    render(top, size.Width, size.Height);
                    ObserveSettings(top, category, inspectReachability: true);
                    scroll.ScrollToBottom();
                    string bottom = $"{image++:00}-clean-settings-{category.ToLowerInvariant()}-{size.Label}-end.png";
                    render(bottom, size.Width, size.Height);
                    ObserveSettings(bottom, category, inspectReachability: false);
                }
            }
            Category("Audio");
            Page<ComboBox>("CmbAudioMode").SelectedIndex = 0;
            scroll.ScrollToTop();
            string systemAudio = $"{image++:00}-clean-settings-system-audio-minimum.png";
            render(systemAudio, 720, 480);
            ObserveSettings(systemAudio, "Audio", inspectReachability: true);
            Category("General");
            Page<CheckBox>("ChkClipSavedSound").IsChecked = true;
            Page<ComboBox>("CmbNotificationTone").SelectedIndex = 2;
            Page<TextBlock>("CustomSoundName").Text = "Synthetic WAV selection.wav";
            Page<Button>("BtnSaveSettings").BringIntoView();
            var soundName = Page<TextBlock>("CustomSoundName");
            scroll.ScrollToVerticalOffset(Math.Max(0, soundName.TranslatePoint(new Point(0, -80), (FrameworkElement)scroll.Content).Y));
            string customSound = $"{image++:00}-clean-settings-custom-sound-minimum.png";
            render(customSound, 720, 480);
            ObserveSettings(customSound, "General", inspectReachability: true);
            if (!IsInRenderedPath(soundName, page))
                throw new InvalidOperationException("Custom WAV settings must remain available when selected.");
            Page<CheckBox>("ChkClipSavedSound").IsChecked = false;
            scroll.ScrollToTop();
            string soundOff = $"{image++:00}-clean-settings-sound-off-minimum.png";
            render(soundOff, 720, 480);
            ObserveSettings(soundOff, "General", inspectReachability: true);
            if (IsInRenderedPath(soundName, page) || IsInRenderedPath(Page<ComboBox>("CmbNotificationTone"), page))
                throw new InvalidOperationException("Disabled notification sound should collapse its inactive options.");
            Invoke("ReturnToClipWorkspace");

            Invoke("OpenConnectedFolders");
            var folders = (ConnectedFoldersWindow)typeof(MainWindow).GetField("_foldersPage", Private)!.GetValue(window)!;
            var list = (ListBox)folders.FindName("FolderList");
            vm.ConnectedFolders.Clear();
            for (int index = 0; index < 24; index++)
                vm.ConnectedFolders.Add(new ConnectedClipFolder {
                    DisplayName = $"Synthetic linked folder {index + 1:00}",
                    Path = Path.Combine(fixtures, "Synthetic linked folders", $"Folder {index + 1:00}")
                });
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.ImportStatusText))!.SetValue(vm, "Synthetic folder list · no scan performed");
            list.SelectedIndex = 0;
            foreach (var size in new[] { (Width: 1280, Height: 720, Label: "wide"), (Width: 720, Height: 480, Label: "minimum") })
            {
                string file = $"{image++:00}-clean-folders-{size.Label}.png";
                render(file, size.Width, size.Height);
                Rect bounds = new(folders.RenderSize);
                bool actionsFit = ProductVisuals(folders).OfType<Button>().Where(button => IsInRenderedPath(button, folders))
                    .All(button => ProductContains(bounds, RelativeBounds(button, folders)));
                bool listFits = ProductContains(bounds, RelativeBounds(list, folders)) && list.ActualHeight >= 96;
                var surface = ProductVisuals(list).OfType<ScrollViewer>().Single();
                bool singleSurface = !ProductVisuals(folders).OfType<ScrollViewer>()
                    .Where(candidate => IsInRenderedPath(candidate, folders)).Any(candidate => candidate != surface);
                list.ScrollIntoView(vm.ConnectedFolders[^1]);
                DrainDispatcherThroughContextIdle(folders.Dispatcher);
                folders.UpdateLayout();
                bool lastFolderReachable = list.ItemContainerGenerator.ContainerFromIndex(vm.ConnectedFolders.Count - 1) is FrameworkElement last &&
                    ProductContains(RelativeBounds(list, folders), RelativeBounds(last, folders));
                observations.Add(new { Image = file, Category = "Connected folders", ActionsFit = actionsFit,
                    ListFits = listFits, SingleScrollSurface = singleSurface, LastFolderReachable = lastFolderReachable,
                    FolderCount = vm.ConnectedFolders.Count, ListHeight = list.ActualHeight, surface.ScrollableHeight });
                WriteEvidence();
                if (!actionsFit || !listFits || !singleSurface || !lastFolderReachable)
                    throw new InvalidOperationException("Connected-folder list/actions do not fit or scroll cleanly in " + file);
            }
        }
        finally
        {
            Invoke("ReturnToClipWorkspace");
            page.LeavePage();
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.ImportStatusText))!.SetValue(vm, importStatusBefore);
            restored = profileBefore.SequenceEqual(File.ReadAllBytes(profile)) && !vm.IsImporting &&
                !vm.CaptureHost.IsRunning && !vm.DiagnosticsInitialized && prohibitedActions == 0;
            WriteEvidence();
            if (!restored) throw new InvalidOperationException("Clean Settings fixtures changed preferences or started a live subsystem.");
        }

        void ObserveSettings(string file, string category, bool inspectReachability)
        {
            Rect pageBounds = new(page.RenderSize);
            var save = Page<Button>("BtnSaveSettings");
            var cancel = Page<Button>("BtnCancelSettings");
            Rect saveBefore = RelativeBounds(save, page), cancelBefore = RelativeBounds(cancel, page);
            Rect scrollBounds = RelativeBounds(scroll, page);
            bool fixedActions = ProductContains(pageBounds, saveBefore) && ProductContains(pageBounds, cancelBefore) &&
                !HasVisualAncestor(save, scroll, page) && !HasVisualAncestor(cancel, scroll, page) &&
                scrollBounds.Bottom <= saveBefore.Top + 1;
            var navigation = new[] { "NavRecording", "NavGames", "NavAudio", "NavShortcuts", "NavStorage", "NavGeneral", "NavSharing" }
                .Select(name => Page<Button>(name)).ToArray();
            bool fixedNavigation = navigation.All(button => ProductContains(pageBounds, RelativeBounds(button, page)) &&
                !HasVisualAncestor(button, scroll, page));
            bool finiteViewport = scroll.ViewportHeight > 0 && double.IsFinite(scroll.ViewportHeight) && scroll.ViewportWidth > 0 &&
                double.IsFinite(scroll.ViewportWidth) && ProductContains(pageBounds, scrollBounds);
            var nested = ProductVisuals(scroll).OfType<ScrollViewer>().Where(candidate => candidate != scroll &&
                IsInRenderedPath(candidate, page) && candidate.ScrollableHeight > .5 &&
                candidate.VerticalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible).ToArray();
            bool longPathPreserved = true;
            if (category == "Games")
            {
                var executable = Page<TextBlock>("TxtGameExecutable");
                longPathPreserved = executable.Text == staged.ApprovedGameProfiles[0].ExecutablePath &&
                    Convert.ToString(executable.ToolTip) == executable.Text && executable.TextTrimming == TextTrimming.CharacterEllipsis;
            }
            var controls = ProductVisuals((FrameworkElement)scroll.Content).OfType<Control>()
                .Where(control => control is Button or CheckBox or ComboBox or TextBox or PasswordBox or Expander)
                .Where(control => IsInRenderedPath(control, page)).ToArray();
            var reachability = new List<object>();
            bool allReachable = true;
            if (inspectReachability)
            {
                double before = scroll.VerticalOffset;
                foreach (var control in controls)
                {
                    control.BringIntoView();
                    for (int pass = 0; pass < 3; pass++)
                    {
                        DrainDispatcherThroughContextIdle(page.Dispatcher);
                        page.UpdateLayout();
                    }
                    Rect location = RelativeBounds(control, page);
                    // An expanded disclosure includes its entire potentially
                    // long body. Its own header is independently reachable;
                    // every actual child input/action is checked below.
                    if (control is Expander) continue;
                    bool contained = ProductContains(scrollBounds, location);
                    reachability.Add(new { Name = control.Name, Kind = control.GetType().Name,
                        Caption = Convert.ToString(control is ContentControl content ? content.Content : null),
                        Reachable = contained, location.Left, location.Top, location.Width, location.Height });
                    allReachable &= contained;
                }
                scroll.ScrollToVerticalOffset(before);
                DrainDispatcherThroughContextIdle(page.Dispatcher);
                page.UpdateLayout();
                fixedActions &= saveBefore == RelativeBounds(save, page) && cancelBefore == RelativeBounds(cancel, page);
            }
            observations.Add(new { Image = file, Category = category, FixedActions = fixedActions,
                FixedNavigation = fixedNavigation, FinitePageViewport = finiteViewport,
                NestedVerticalScrollSurfaceCount = nested.Length, AllInputsAndActionsReachable = allReachable,
                FullExecutablePathPreservedInTooltip = longPathPreserved,
                InputAndActionCount = controls.Length, Reachability = reachability,
                scroll.ViewportHeight, scroll.ScrollableHeight, scroll.VerticalOffset,
                VisibleTextBlockCount = ProductVisuals(scroll).OfType<TextBlock>().Count(text => IsInRenderedPath(text, page)),
                StagedOnly = true });
            WriteEvidence();
            if (!fixedActions || !fixedNavigation || !finiteViewport || nested.Length != 0 || !allReachable || !longPathPreserved)
                throw new InvalidOperationException("Settings actions/navigation, nested scrolling, or input reachability regressed in " + file);
        }

        void WriteEvidence() => File.WriteAllText(Path.Combine(output, "clean-settings-fixture-evidence.json"), JsonSerializer.Serialize(new {
            Scope = "Actual production WPF pages with synthetic staged game/audio/folder identities. No Save, scans, approvals, endpoints, network calls, capture or startup registration.",
            SettingsSaved = false, ProhibitedActionsRequested = prohibitedActions,
            OriginalProfileAndSubsystemStatePreserved = restored,
            Observations = observations
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Rect RelativeBounds(FrameworkElement child, FrameworkElement ancestor) =>
        child.TransformToAncestor(ancestor).TransformBounds(new Rect(child.RenderSize));

    private static bool HasVisualAncestor(DependencyObject child, DependencyObject ancestor, DependencyObject boundary)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(child); current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
            if (ReferenceEquals(current, boundary)) return false;
        }
        return false;
    }

    private static bool IsInRenderedPath(FrameworkElement child, FrameworkElement boundary)
    {
        for (DependencyObject? current = child; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement element && element.Visibility != Visibility.Visible) return false;
            if (ReferenceEquals(current, boundary)) return child.ActualWidth > 0 && child.ActualHeight > 0;
        }
        return false;
    }
}
