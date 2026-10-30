using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;

namespace Flick.UiRender;

internal static partial class Program
{
    private static IReadOnlyList<string> RenderUXNavigationFixtures(MainWindow window, MainViewModel vm,
        AppSettings settings, string fixtures, string output, Action<string, int, int> render)
    {
        void Invoke(string method, params object?[] values) => typeof(MainWindow).GetMethod(method, Private)!
            .Invoke(window, values);
        T Main<T>(string name) => (T)window.FindName(name);
        string profile = AppSettings.GetSettingsFilePath();
        byte[] originalProfileHash = SHA256.HashData(File.ReadAllBytes(profile));
        string originalLiveSettings = JsonSerializer.Serialize(vm.Settings);
        var modeField = typeof(MainWindow).GetField("_layoutMode", Private)!;
        var editorField = typeof(MainWindow).GetField("_compactShowingEditor", Private)!;
        object? originalMode = modeField.GetValue(window);
        object? originalEditor = editorField.GetValue(window);
        var navigation = typeof(MainWindow).GetMethod("CompleteSavedSettingsNavigation", Private)
            ?? throw new InvalidOperationException("Missing production saved-settings navigation.");
        var save = typeof(SettingsWindow).GetMethod("Save_Click", Private)!;
        var failures = new List<string>();
        var observations = new List<object>();
        int forbiddenCalls = 0, verifiedWrites = 0, startupStubCalls = 0, closeRequests = 0, savedCloseRequests = 0;
        SettingsWindow? page = null;
        bool restored = false;
        try
        {
            // A valid save uses only a cloned model and an isolated JSON file.
            // Its CloseRequested callback invokes the real navigation only;
            // it never enters hotkey registration or replay application.
            string savedFile = Path.Combine(fixtures, "settings-navigation", "verified", "settings.json");
            bool verifiedBeforeNavigation = false;
            page = MakePage("verified", reviewed =>
            {
                reviewed.SaveReviewedToFile(savedFile);
                using var expected = JsonDocument.Parse(JsonSerializer.Serialize(reviewed));
                using var persisted = JsonDocument.Parse(File.ReadAllText(savedFile));
                verifiedBeforeNavigation = JsonElement.DeepEquals(expected.RootElement, persisted.RootElement);
                verifiedWrites++;
            });
            page.CloseRequested += saved =>
            {
                closeRequests++;
                if (!saved) return;
                savedCloseRequests++;
                if (verifiedBeforeNavigation) navigation.Invoke(window, null);
            };
            BeginFromOpenEditor(page);
            ((CheckBox)page.FindName("ChkStartWithWindows")).IsChecked = true;
            typeof(SettingsWindow).GetMethod("SelectCategory", Private)!.Invoke(page, ["General"]);
            render("142-settings-before-save-minimum.png", 720, 480);
            save.Invoke(page, [page.FindName("BtnSaveSettings"), new RoutedEventArgs()]);
            bool dashboardShown = DashboardShown();
            bool editorClosed = Main<FrameworkElement>("EditorContextLayer").Visibility != Visibility.Visible;
            bool settingsReleased = typeof(MainWindow).GetField("_settingsPage", Private)!.GetValue(window) == null;
            if (!verifiedBeforeNavigation || verifiedWrites != 1 || savedCloseRequests != 1 ||
                !dashboardShown || !editorClosed || !settingsReleased || !File.Exists(savedFile))
                failures.Add("Successful verified Settings Save did not release Settings and return to the compact dashboard.");
            render("143-settings-saved-dashboard-minimum.png", 720, 480);
            observations.Add(new { Image = "143-settings-saved-dashboard-minimum.png", VerifiedWriteBeforeClose = verifiedBeforeNavigation,
                DashboardShown = dashboardShown, EditorToolClosed = editorClosed, SettingsPageReleased = settingsReleased,
                FullCloseSettingsPageWasNotCalled = true, SavedFileIsIsolated = savedFile.StartsWith(fixtures, StringComparison.OrdinalIgnoreCase) });
            modeField.SetValue(window, WorkspaceLayoutMode.Split);
            Invoke("ApplyWorkspaceLayout");
            render("144-settings-saved-dashboard-wide.png", 1280, 720);

            // Invalid input must preserve the live page and exact draft, with
            // no save callback or navigation. This path does not show a dialog.
            page = MakePage("invalid", _ => verifiedWrites++);
            page.CloseRequested += saved => { closeRequests++; if (saved) savedCloseRequests++; };
            BeginFromOpenEditor(page);
            var duration = (TextBox)page.FindName("TxtDuration");
            duration.Text = "not a number";
            int writesBefore = verifiedWrites, closesBefore = closeRequests;
            save.Invoke(page, [page.FindName("BtnSaveSettings"), new RoutedEventArgs()]);
            bool pageRetained = ReferenceEquals(page, typeof(MainWindow).GetField("_settingsPage", Private)!.GetValue(window)) &&
                Main<FrameworkElement>("WorkspacePageLayer").Visibility == Visibility.Visible;
            bool draftRetained = duration.Text == "not a number";
            string validation = ((TextBlock)page.FindName("TxtSaveStatus")).Text;
            if (!pageRetained || !draftRetained || writesBefore != verifiedWrites || closesBefore != closeRequests ||
                string.IsNullOrWhiteSpace(validation))
                failures.Add("Invalid Settings Save discarded its draft, navigated, wrote settings or omitted feedback.");
            render("145-settings-invalid-save-minimum.png", 720, 480);
            observations.Add(new { Image = "145-settings-invalid-save-minimum.png", PageRetained = pageRetained,
                DraftRetained = draftRetained, NoWrite = writesBefore == verifiedWrites,
                NoCloseRequest = closesBefore == closeRequests, Validation = validation });
        }
        finally
        {
            Invoke("ReturnToClipWorkspace");
            page?.LeavePage();
            Invoke("CloseEditorContext");
            modeField.SetValue(window, originalMode);
            editorField.SetValue(window, originalEditor);
            Invoke("ApplyWorkspaceLayout");
            restored = originalProfileHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profile))) &&
                originalLiveSettings == JsonSerializer.Serialize(vm.Settings) && !vm.CaptureHost.IsRunning &&
                !vm.DiagnosticsInitialized && forbiddenCalls == 0;
            File.WriteAllText(Path.Combine(output, "settings-navigation-evidence.json"), JsonSerializer.Serialize(new {
                UsesActualSaveAndNavigationMethods = true, FullCloseSettingsPageWasNotCalled = true,
                VerifiedWritesToIsolatedFiles = verifiedWrites, WindowsStartupStubCalls = startupStubCalls,
                CloseRequests = closeRequests, SavedCloseRequests = savedCloseRequests, ForbiddenLiveCalls = forbiddenCalls,
                OriginalProfileAndLiveSettingsUnchanged = restored, CapturePerformed = false,
                Observations = observations, Failures = failures
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!restored) throw new InvalidOperationException("Settings navigation fixtures changed the profile or started a live subsystem.");
        }
        return failures;

        SettingsWindow MakePage(string name, Action<AppSettings> writer)
        {
            var clone = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            string directory = Path.Combine(fixtures, "settings-navigation", name);
            clone.StorageRoot = directory;
            clone.OriginalsFolder = Path.Combine(directory, "Originals");
            clone.ExportsFolder = Path.Combine(directory, "Exports");
            clone.TempFolder = Path.Combine(directory, "Temp");
            clone.StartWithWindows = false;
            clone.GameDetectionEnabled = false;
            clone.ApprovedGameProfiles = [];
            clone.EnableDiscordWebhook = false;
            clone.MaskedWebhookUrl = "";
            clone.AudioMode = "SystemFallback";
            clone.GameAudioSource = clone.VoiceAudioSource = null;
            clone.EnableMicrophone = false;
            clone.SystemAudioDeviceId = clone.MicrophoneDeviceId = "default";
            return new SettingsWindow(clone, () => false,
                () => { forbiddenCalls++; throw new InvalidOperationException("No app discovery in navigation fixtures."); },
                _ => { forbiddenCalls++; throw new InvalidOperationException("No endpoint discovery in navigation fixtures."); },
                writer, (_, _) => startupStubCalls++);
        }

        void BeginFromOpenEditor(SettingsWindow target)
        {
            Invoke("ReturnToClipWorkspace");
            Invoke("CloseEditorContext");
            modeField.SetValue(window, WorkspaceLayoutMode.Compact);
            Invoke("ShowCompactEditor");
            Invoke("EditorTool_Click", Main<Button>("ToolShare"), new RoutedEventArgs());
            if (Main<FrameworkElement>("EditorContextLayer").Visibility != Visibility.Visible)
                throw new InvalidOperationException("Navigation fixture did not begin with an open editor tool.");
            typeof(MainWindow).GetField("_settingsPage", Private)!.SetValue(window, target);
            Invoke("ShowWorkspacePage", target);
        }

        bool DashboardShown() => Main<FrameworkElement>("WorkspacePageLayer").Visibility != Visibility.Visible &&
            Main<FrameworkElement>("WorkspaceContentGrid").Visibility == Visibility.Visible &&
            Main<FrameworkElement>("ClipLibraryPanel").Visibility == Visibility.Visible &&
            Main<FrameworkElement>("ClipEditorPanel").Visibility == Visibility.Collapsed &&
            Main<ContentControl>("WorkspacePageHost").Content == null;
    }
}
