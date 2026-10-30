using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalClip.App.Views;
using LocalClip.App.Services;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>
/// Actual production Settings Save/Cancel routed through the real WPF controls.
/// File writes and Windows HKCU startup registration are injected into isolated
/// test sinks. No real user profile, video, registry or process is modified.
/// Called from the suite's existing single-Application STA fixture.
/// </summary>
internal static class SettingsPersistenceAssertions
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "Flick-ReviewedSettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "reviewed.json");
        try
        {
            SettingsAudioPersistenceAssertions.Run(root);
            RepeatStartupSaveRepairsRegistration(root);
            FailedDiskSaveRetainsStartupDraftAndRollsBackRegistration(root);
            var source = AppSettings.CreateDefault();
            source.OriginalsFolder = Path.Combine(root, "originals");
            source.ExportsFolder = Path.Combine(root, "exports");
            int startupAttempts = 0;
            var page = new SettingsWindow(source, () => false,
                saveReviewedSettings: s => s.SaveReviewedToFile(file),
                applyWindowsStartup: (enabled, minimized) =>
                {
                    startupAttempts++;
                    if (enabled) throw new UnauthorizedAccessException("Synthetic startup policy restriction");
                });

            CheckBox Checked(string name) => Assert.IsType<CheckBox>(page.FindName(name));
            Assert.True(Checked("ChkShowResourceUsage").IsChecked);
            Assert.False(Checked("ChkStartMinimized").IsChecked);
            Assert.True(Checked("ChkKeepReplayReadyInBackground").IsChecked);
            Checked("ChkShowResourceUsage").IsChecked = false;
            Checked("ChkStartMinimized").IsChecked = true;
            Checked("ChkStartWithWindows").IsChecked = true;
            Checked("ChkKeepReplayReadyInBackground").IsChecked = false;
            Checked("ChkPreviewUpdates").IsChecked = false;
            Checked("ChkClipSavedOverlay").IsChecked = false;
            Checked("ChkClipSavedSound").IsChecked = false;
            Assert.Contains("tray", Checked("ChkStartMinimized").Content?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Contains("tray", Checked("ChkStartMinimized").ToolTip?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            Assert.IsType<TextBox>(page.FindName("TxtDuration")).Text = "60";
            Assert.IsType<ComboBox>(page.FindName("CmbQuality")).SelectedIndex = 2;
            Assert.IsType<ComboBox>(page.FindName("CmbShareCap")).SelectedIndex = 2;
            Assert.IsType<TextBox>(page.FindName("TxtSaveHotkey")).Text = "Ctrl+Shift+K";

            bool closedAndSaved = false;
            page.CloseRequested += saved => closedAndSaved = saved;
            typeof(SettingsWindow).GetMethod("Save_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(page, new object[] { page, new RoutedEventArgs() });

            Assert.True(closedAndSaved);
            Assert.Equal(1, startupAttempts);
            Assert.False(source.StartWithWindows); // Failed OS operation never falsely persists success.
            Assert.True(source.StartMinimizedToTray);
            Assert.False(source.KeepReplayReadyInBackground);
            Assert.False(source.ShowResourceUsage);
            Assert.False(source.IncludePreviewUpdates);
            Assert.False(source.ShowClipSavedOverlay);
            Assert.False(source.PlayClipSavedSound);
            Assert.Equal(60, source.ReplayDurationSeconds);
            Assert.Equal(50, source.DiscordTargetSizeMb);
            Assert.Equal("Ctrl+Shift+K", source.SaveReplayHotkey);
            Assert.Contains("startup", page.LastSaveWarning, StringComparison.OrdinalIgnoreCase);

            var disk = AppSettings.LoadExistingFile(file);
            Assert.False(disk.RequiresExplicitSettingsSave);
            Assert.False(disk.StartWithWindows);
            Assert.True(disk.StartMinimizedToTray);
            Assert.False(disk.KeepReplayReadyInBackground);
            Assert.False(disk.ShowResourceUsage);
            Assert.False(disk.IncludePreviewUpdates);
            Assert.False(disk.ShowClipSavedOverlay);
            Assert.False(disk.PlayClipSavedSound);
            Assert.Equal(60, disk.ReplayDurationSeconds);
            Assert.Equal("Smooth", disk.QualityPreset);
            Assert.Equal("Ctrl+Shift+K", disk.SaveReplayHotkey);
            Assert.Equal(50, disk.DiscordTargetSizeMb);
            Assert.Equal(source.OriginalsFolder, disk.OriginalsFolder);
            Assert.Equal(source.ExportsFolder, disk.ExportsFolder);

            // Opening another real Settings page must display the *persisted*
            // selections rather than fresh default checkbox values.
            var reopened = new SettingsWindow(disk, () => false,
                saveReviewedSettings: s => s.SaveReviewedToFile(file),
                applyWindowsStartup: (_, _) => throw new InvalidOperationException("No startup change expected"));
            Assert.True(CheckedAgain("ChkStartMinimized"));
            Assert.False(CheckedAgain("ChkKeepReplayReadyInBackground"));
            Assert.False(CheckedAgain("ChkShowResourceUsage"));
            Assert.False(CheckedAgain("ChkPreviewUpdates"));
            Assert.False(CheckedAgain("ChkClipSavedSound"));
            bool CheckedAgain(string name) => Assert.IsType<CheckBox>(reopened.FindName(name)).IsChecked == true;
            Assert.Equal("60", Assert.IsType<TextBox>(reopened.FindName("TxtDuration")).Text);
            Assert.Equal(2, Assert.IsType<ComboBox>(reopened.FindName("CmbShareCap")).SelectedIndex);

            // A change on a later page followed by Cancel never changes the
            // backing object or persisted JSON.
            var before = File.ReadAllText(file);
            Assert.IsType<CheckBox>(reopened.FindName("ChkShowResourceUsage")).IsChecked = true;
            Assert.IsType<CheckBox>(reopened.FindName("ChkKeepReplayReadyInBackground")).IsChecked = true;
            reopened.CloseRequested += saved => Assert.False(saved);
            typeof(SettingsWindow).GetMethod("Cancel_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(reopened, new object[] { reopened, new RoutedEventArgs() });
            Assert.Equal(before, File.ReadAllText(file));
            Assert.False(disk.ShowResourceUsage);
            Assert.False(disk.KeepReplayReadyInBackground);

            // Storing a new startup preference is independent from Minimize
            // and Show RAM and CPU, and only the user-reviewed Save touches HKCU.
            var successfulStartup = new SettingsWindow(disk, () => false,
                saveReviewedSettings: s => s.SaveReviewedToFile(file),
                applyWindowsStartup: (enabled, minimized) =>
                {
                    startupAttempts++;
                    Assert.True(enabled);
                    Assert.True(minimized);
                });
            Assert.IsType<CheckBox>(successfulStartup.FindName("ChkStartWithWindows")).IsChecked = true;
            bool complete = false;
            successfulStartup.CloseRequested += saved => complete = saved;
            typeof(SettingsWindow).GetMethod("Save_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(successfulStartup, new object[] { successfulStartup, new RoutedEventArgs() });
            Assert.True(complete);
            Assert.Equal(2, startupAttempts);
            Assert.True(AppSettings.LoadExistingFile(file).StartWithWindows);
            Assert.True(AppSettings.LoadExistingFile(file).StartMinimizedToTray);
        }
        finally
        {
            // Only the test-owned unique directory may be cleaned up.
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void RepeatStartupSaveRepairsRegistration(string root)
    {
        var settings = AppSettings.CreateDefault();
        settings.OriginalsFolder = Path.Combine(root, "repeat-startup-originals");
        settings.ExportsFolder = Path.Combine(root, "repeat-startup-exports");
        settings.StartWithWindows = true;
        settings.StartMinimizedToTray = true;
        string file = Path.Combine(root, "repeat-startup.json");
        settings.SaveReviewedToFile(file);
        int repairs = 0;
        bool persistedBeforeClose = false;
        var page = new SettingsWindow(settings, () => false,
            saveReviewedSettings: staged => { staged.SaveReviewedToFile(file); persistedBeforeClose = true; },
            applyWindowsStartup: (enabled, tray) => { Assert.True(enabled); Assert.True(tray); repairs++; },
            readWindowsStartupStatus: _ => new(WindowsStartupRegistrationState.Registered, "Registered for Windows startup."));
        page.CloseRequested += saved => { Assert.True(saved); Assert.True(persistedBeforeClose); };
        Save(page);
        Assert.Equal(1, repairs); // Already-enabled JSON must still repair/check the OS entry.
        Save(page);
        Assert.Equal(2, repairs);
        Assert.True(AppSettings.LoadExistingFile(file).StartWithWindows);
        Assert.Empty(page.LastSaveWarning);

        // Windows-disabled approval does not erase an explicit desired opt-in
        // or unrelated preferences, and the caller receives a clear warning.
        page = new SettingsWindow(settings, () => false,
            saveReviewedSettings: staged => staged.SaveReviewedToFile(file),
            applyWindowsStartup: (_, _) => repairs++,
            readWindowsStartupStatus: _ => new(WindowsStartupRegistrationState.DisabledByWindows,
                "Windows disabled Flick at startup. Enable it in Windows Startup apps."));
        Assert.IsType<CheckBox>(page.FindName("ChkShowResourceUsage")).IsChecked = false;
        bool savedWithWarning = false;
        page.CloseRequested += saved => savedWithWarning = saved;
        Save(page);
        Assert.True(savedWithWarning);
        Assert.Contains("Windows disabled", page.LastSaveWarning);
        Assert.True(AppSettings.LoadExistingFile(file).StartWithWindows);
        Assert.False(AppSettings.LoadExistingFile(file).ShowResourceUsage);
    }

    private static void Save(SettingsWindow page) => typeof(SettingsWindow)
        .GetMethod("Save_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(page, new object[] { page, new RoutedEventArgs() });

    private static void FailedDiskSaveRetainsStartupDraftAndRollsBackRegistration(string root)
    {
        var settings = AppSettings.CreateDefault();
        settings.OriginalsFolder = Path.Combine(root, "startup-write-failure-originals");
        settings.ExportsFolder = Path.Combine(root, "startup-write-failure-exports");
        string file = Path.Combine(root, "startup-write-failure.json");
        settings.SaveReviewedToFile(file);
        string before = File.ReadAllText(file);
        var applied = new List<bool>();
        var page = new SettingsWindow(settings, () => false,
            saveReviewedSettings: _ => throw new IOException("Synthetic disk write failure"),
            applyWindowsStartup: (enabled, _) => applied.Add(enabled));
        Assert.IsType<CheckBox>(page.FindName("ChkStartWithWindows")).IsChecked = true;
        bool closed = false;
        page.CloseRequested += _ => closed = true;
        Save(page);
        Assert.False(closed);
        Assert.Equal(new[] { true, false }, applied);
        Assert.False(settings.StartWithWindows);
        Assert.True(Assert.IsType<CheckBox>(page.FindName("ChkStartWithWindows")).IsChecked);
        Assert.Equal(before, File.ReadAllText(file));
        Assert.Contains("changes are still here", Assert.IsType<TextBlock>(page.FindName("TxtSaveStatus")).Text);
    }
}
