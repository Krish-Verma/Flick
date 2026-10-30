using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.Models;

namespace Flick.UiRender;

internal static partial class Program
{
    private static IReadOnlyList<string> RenderShortcutCaptureFixtures(MainWindow window, MainViewModel vm,
        AppSettings settings, string fixtures, string output, Action<string, int, int> render)
    {
        string profile = AppSettings.GetSettingsFilePath();
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(profile));
        string originalLive = JsonSerializer.Serialize(vm.Settings);
        string directory = Path.Combine(fixtures, "shortcut-capture");
        var clone = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        clone.StorageRoot = directory;
        clone.OriginalsFolder = Path.Combine(directory, "Originals");
        clone.ExportsFolder = Path.Combine(directory, "Exports");
        clone.TempFolder = Path.Combine(directory, "Temp");
        clone.StartWithWindows = false;
        clone.GameDetectionEnabled = false;
        clone.ApprovedGameProfiles = [];
        clone.AudioMode = "SystemFallback";
        clone.GameAudioSource = clone.VoiceAudioSource = null;
        clone.SystemAudioDeviceId = clone.MicrophoneDeviceId = "default";
        clone.EnableMicrophone = false;
        clone.EnableDiscordWebhook = false;
        clone.MaskedWebhookUrl = "";
        clone.CaptureToggleHotkey = "Ctrl+Shift+F9";
        clone.SaveReplayHotkey = "Ctrl+Shift+F8";
        var failures = new List<string>();
        var observations = new List<object>();
        int forbiddenCalls = 0, writes = 0;
        bool editing = false, closedSaved = false, restored = false;
        string isolatedFile = Path.Combine(directory, "settings.json");
        var page = new SettingsWindow(clone, () => false,
            () => { forbiddenCalls++; throw new InvalidOperationException("No app enumeration."); },
            _ => { forbiddenCalls++; throw new InvalidOperationException("No endpoint enumeration."); },
            reviewed => { reviewed.SaveReviewedToFile(isolatedFile); writes++; },
            (_, _) => { forbiddenCalls++; throw new InvalidOperationException("No Windows startup changes."); });
        page.ShortcutEditingChanged += value => editing = value;
        page.CloseRequested += saved => closedSaved = saved;
        T Page<T>(string name) => (T)page.FindName(name);
        void Call(string method, params object?[] args) => typeof(SettingsWindow).GetMethod(method, Private)!.Invoke(page, args);
        void Main(string method, params object?[] args) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, args);
        void Begin(string name) => Call("ChangeShortcut_Click", Page<Button>(name), new RoutedEventArgs());
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        void Observe(string image) => observations.Add(new {
            Image = image, Editing = editing, Toggle = Page<TextBox>("TxtToggleHotkey").Text,
            Save = Page<TextBox>("TxtSaveHotkey").Text,
            Hint = Page<TextBlock>("ShortcutHint").Text,
            ToggleReadOnly = Page<TextBox>("TxtToggleHotkey").IsReadOnly,
            SaveReadOnly = Page<TextBox>("TxtSaveHotkey").IsReadOnly,
            LogicalFocusName = (FocusManager.GetFocusedElement(FocusManager.GetFocusScope(page)) as FrameworkElement)?.Name,
            Writes = writes
        });
        try
        {
            Main("ReturnToClipWorkspace");
            typeof(MainWindow).GetField("_settingsPage", Private)!.SetValue(window, page);
            Main("ShowWorkspacePage", page);
            Call("SelectCategory", "Shortcuts");
            render("146-shortcuts-rest-minimum.png", 720, 480);
            Observe("146-shortcuts-rest-minimum.png");
            Begin("BtnEditToggleHotkey");
            Check(editing && Page<TextBox>("TxtToggleHotkey").Text == "Press a shortcut…" &&
                Page<TextBox>("TxtSaveHotkey").Text == "Ctrl+Shift+F8", "Edit did not begin capture in the selected field.");
            render("147-shortcuts-listening-minimum.png", 720, 480);
            Observe("147-shortcuts-listening-minimum.png");
            Call("CaptureShortcut", Key.F8, ModifierKeys.Control | ModifierKeys.Shift);
            Check(editing && Page<TextBlock>("ShortcutHint").Text.Contains("other shortcut"),
                "Duplicate chord did not retain capture with a concise conflict message.");
            render("148-shortcuts-conflict-minimum.png", 720, 480);
            Observe("148-shortcuts-conflict-minimum.png");
            Begin("BtnEditSaveHotkey");
            Check(Page<TextBox>("TxtToggleHotkey").Text == "Ctrl+Shift+F9", "Switching fields lost the previous draft.");
            render("149-shortcuts-save-listening-wide.png", 1280, 720);
            Observe("149-shortcuts-save-listening-wide.png");
            Call("CaptureShortcut", Key.K, ModifierKeys.Control | ModifierKeys.Alt);
            Check(!editing && Page<TextBox>("TxtSaveHotkey").Text == "Ctrl+Alt+K" &&
                clone.SaveReplayHotkey == "Ctrl+Shift+F8" && writes == 0,
                "Valid capture failed to return to a staged read-only display.");
            render("150-shortcuts-captured-minimum.png", 720, 480);
            Observe("150-shortcuts-captured-minimum.png");
            Begin("BtnEditToggleHotkey");
            Call("Save_Click", Page<Button>("BtnSaveSettings"), new RoutedEventArgs());
            Check(!editing && closedSaved && writes == 1 && File.Exists(isolatedFile) &&
                clone.CaptureToggleHotkey == "Ctrl+Shift+F9" && clone.SaveReplayHotkey == "Ctrl+Alt+K" &&
                !File.ReadAllText(isolatedFile).Contains("Press a shortcut"),
                "Settings Save did not cancel the prompt before the verified isolated write.");
        }
        finally
        {
            Main("ReturnToClipWorkspace");
            page.LeavePage();
            restored = originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profile))) &&
                originalLive == JsonSerializer.Serialize(vm.Settings) && forbiddenCalls == 0 &&
                !vm.CaptureHost.IsRunning && !vm.DiagnosticsInitialized;
            File.WriteAllText(Path.Combine(output, "shortcut-capture-evidence.json"), JsonSerializer.Serialize(new {
                UsesProductionEditAndCaptureHandlers = true, NoSyntheticOsKeypresses = true,
                FocusObservationsAreLogicalOnly = true, OriginalProfileAndLiveSettingsUnchanged = restored,
                IsolatedVerifiedWrites = writes, ForbiddenLiveCalls = forbiddenCalls, CapturePerformed = false,
                Observations = observations, Failures = failures
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!restored) throw new InvalidOperationException("Shortcut fixtures changed the original profile or used a live subsystem.");
        }
        return failures;
    }
}
