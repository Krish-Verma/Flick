using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LocalClip.App.Hotkeys;
using LocalClip.App.Views;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

internal static class ShortcutCaptureAssertions
{
    internal static void Run()
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        string directory = Path.Combine(Path.GetTempPath(), "Flick-shortcut-tests", Guid.NewGuid().ToString("N"));
        var settings = AppSettings.CreateDefault();
        settings.OriginalsFolder = Path.Combine(directory, "Originals");
        settings.ExportsFolder = Path.Combine(directory, "Exports");
        settings.StartWithWindows = false;
        settings.GameDetectionEnabled = false;
        settings.ApprovedGameProfiles = [];
        string originalToggle = settings.CaptureToggleHotkey, originalSave = settings.SaveReplayHotkey;
        int writes = 0, closes = 0, forbiddenCalls = 0;
        AppSettings? persisted = null;
        var page = new SettingsWindow(settings, () => false,
            () => { forbiddenCalls++; throw new InvalidOperationException("No app enumeration."); },
            _ => { forbiddenCalls++; throw new InvalidOperationException("No endpoint enumeration."); },
            reviewed => { writes++; persisted = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(reviewed)); },
            (_, _) => { forbiddenCalls++; throw new InvalidOperationException("No startup changes."); });
        page.CloseRequested += saved => { Assert.True(saved); closes++; };
        bool editing = false;
        page.ShortcutEditingChanged += active => editing = active;
        T Find<T>(string name) => (T)page.FindName(name);
        void Call(string method, params object?[] args) => typeof(SettingsWindow).GetMethod(method, hidden)!.Invoke(page, args);
        void Begin(string target = "Toggle") => Call("ChangeShortcut_Click",
            Find<Button>(target == "Toggle" ? "BtnEditToggleHotkey" : "BtnEditSaveHotkey"), new RoutedEventArgs());
        void Capture(Key key, ModifierKeys modifiers) => Call("CaptureShortcut", key, modifiers);
        var toggle = Find<TextBox>("TxtToggleHotkey");
        var save = Find<TextBox>("TxtSaveHotkey");
        try
        {
            Call("SelectCategory", "Shortcuts");
            FocusManager.SetIsFocusScope(page, true);
            Begin();
            Assert.True(editing);
            Assert.Equal("Press a shortcut…", toggle.Text);
            Assert.True(toggle.IsReadOnly); // Prevent free text/paste; keys are captured instead.
            Assert.Equal(originalSave, save.Text);
            Assert.Same(toggle, FocusManager.GetFocusedElement(FocusManager.GetFocusScope(toggle)));
            Assert.Equal(((SolidColorBrush)page.FindResource("Flick.Brush.TextPrimary")).Color,
                Assert.IsType<SolidColorBrush>(toggle.Foreground).Color);
            Capture(Key.K, ModifierKeys.None);
            Assert.Contains("Ctrl or Alt", Find<TextBlock>("ShortcutHint").Text);
            Assert.True(editing);
            Capture(Key.K, ModifierKeys.Control | ModifierKeys.Windows);
            Assert.Contains("reserved", Find<TextBlock>("ShortcutHint").Text);
            Capture(Key.F8, ModifierKeys.Control | ModifierKeys.Shift);
            Assert.Contains("other shortcut", Find<TextBlock>("ShortcutHint").Text);
            Assert.Equal("Press a shortcut…", toggle.Text);

            // Every modifier combination allowed by the shared policy is
            // preserved exactly; no individual capture persists the model.
            foreach (var modifiers in new[] { ModifierKeys.Control, ModifierKeys.Alt,
                ModifierKeys.Control | ModifierKeys.Shift, ModifierKeys.Alt | ModifierKeys.Shift,
                ModifierKeys.Control | ModifierKeys.Alt,
                ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift })
            {
                if (!editing) Begin();
                Capture(Key.K, modifiers);
                string expected = (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") +
                    (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") +
                    (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + "K";
                Assert.Equal(expected, toggle.Text);
                Assert.True(HotkeyManager.IsValidHotkey(toggle.Text));
                Assert.False(editing);
                Assert.True(toggle.IsReadOnly);
                Assert.Equal(((SolidColorBrush)page.FindResource("Flick.Brush.TextSecondary")).Color,
                    Assert.IsType<SolidColorBrush>(toggle.Foreground).Color);
                Assert.Equal(originalToggle, settings.CaptureToggleHotkey);
                Assert.Equal(0, writes);
            }
            Begin();
            Capture(Key.Tab, ModifierKeys.Control);
            Assert.Equal("Ctrl+Tab", toggle.Text);
            string draft = toggle.Text;
            Begin();
            Capture(Key.Escape, ModifierKeys.None);
            Assert.False(editing);
            Assert.Equal(draft, toggle.Text);

            Begin();
            Begin("Save");
            Assert.Equal(draft, toggle.Text);
            Assert.Equal("Press a shortcut…", save.Text);
            Capture(Key.S, ModifierKeys.Control | ModifierKeys.Alt);
            Assert.Equal("Ctrl+Alt+S", save.Text);
            Assert.Equal(originalSave, settings.SaveReplayHotkey);

            Begin();
            Call("SelectCategory", "General");
            Assert.False(editing);
            Assert.Equal(draft, toggle.Text);
            Call("SelectCategory", "Shortcuts");
            Begin();
            using (var source = new HwndSource(new HwndSourceParameters("Shortcut navigation fixture") {
                Width = 1, Height = 1, WindowStyle = 0 }))
            {
                var tab = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Tab);
                Call("Shortcut_PreviewKeyDown", toggle, tab);
                Assert.False(tab.Handled); // Native Tab navigation remains available.
            }
            Assert.False(editing);
            Assert.Equal(draft, toggle.Text);
            Begin();
            Call("Shortcut_LostKeyboardFocus", toggle,
                new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount,
                    toggle, Find<Button>("BtnEditSaveHotkey")));
            Assert.False(editing);
            Assert.Equal(draft, toggle.Text);
            Begin();
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, page));
            Assert.False(editing);
            Assert.Equal(draft, toggle.Text);

            // Save cancels the literal prompt before staging and commits only
            // completed draft chords, through the normal transaction.
            Begin();
            Call("Save_Click", Find<Button>("BtnSaveSettings"), new RoutedEventArgs());
            Assert.False(editing);
            Assert.Equal(1, writes);
            Assert.Equal(1, closes);
            Assert.Equal(draft, persisted!.CaptureToggleHotkey);
            Assert.Equal("Ctrl+Alt+S", persisted.SaveReplayHotkey);
            Assert.Equal(draft, settings.CaptureToggleHotkey);
            Assert.Equal("Ctrl+Alt+S", settings.SaveReplayHotkey);
            Assert.DoesNotContain("Press a shortcut", JsonSerializer.Serialize(persisted));
            Assert.Equal(0, forbiddenCalls);
        }
        finally
        {
            page.LeavePage();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
