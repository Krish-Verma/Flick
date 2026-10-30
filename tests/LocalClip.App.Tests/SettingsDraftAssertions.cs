using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalClip.App.Services;
using LocalClip.App.Views;
using LocalClip.Core.Audio;
using LocalClip.Core.Models;
using LocalClip.Core.GameDetection;
using Xunit;

namespace LocalClip.App.Tests;

// Called on the existing suite's STA. Every file, endpoint observation and
// startup observation is isolated; this never uses the owner's profile or HKCU.
internal static class SettingsDraftAssertions
{
    public static void Run(string root)
    {
        ChangedRevertedAndInvalidDrafts(root);
        HiddenInvalidDraftReviewReturnsToField(root);
        GameSelectionAndRevertedEdits(root);
        ReviewedSaveReopenAndCancel(root);
        RepairAndRecoveredReviewRemainSaveable(root);
        DisconnectedDevicesPermitUnrelatedDrafts(root);
    }

    private static void ChangedRevertedAndInvalidDrafts(string root)
    {
        var source = Preferences(root, "draft-state");
        int startupReads = 0, endpointReads = 0, writes = 0;
        var page = new SettingsWindow(source, () => false,
            loadAudioEndpoints: _ => { endpointReads++; throw new InvalidOperationException("No discovery expected"); },
            saveReviewedSettings: _ => writes++, applyWindowsStartup: (_, _) => throw new InvalidOperationException("No registry expected"),
            readWindowsStartupStatus: _ => { startupReads++; return new(WindowsStartupRegistrationState.Registered, "Registered"); });
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.CanSaveDraft);
        Assert.False(Find<Button>(page, "BtnSaveSettings").IsEnabled);
        var resources = Find<CheckBox>(page, "ChkShowResourceUsage");
        resources.IsChecked = !source.ShowResourceUsage;
        Assert.True(page.HasUnsavedChanges);
        Assert.True(page.CanSaveDraft);
        Assert.Equal("Unsaved changes", Find<TextBlock>(page, "TxtSaveStatus").Text);
        resources.IsChecked = source.ShowResourceUsage;
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.CanSaveDraft);
        var duration = Find<TextBox>(page, "TxtDuration");
        Assert.Contains("MiB", Find<TextBlock>(page, "TxtBufferWarning").Text);
        duration.Text = "bad";
        Assert.True(page.HasUnsavedChanges);
        Assert.False(page.CanSaveDraft);
        Assert.Equal(Visibility.Visible, Find<TextBlock>(page, "TxtDurationError").Visibility);
        Assert.Contains("15–120", Find<TextBlock>(page, "TxtDurationError").Text);
        Assert.Empty(Find<TextBlock>(page, "TxtBufferWarning").Text);
        Assert.Null(Find<TextBlock>(page, "TxtBufferWarning").ToolTip);
        Assert.Equal(source.AllowSoftwareFallback, Find<CheckBox>(page, "ChkSoftwareFallback").IsChecked);
        bool closed = false;
        page.CloseRequested += _ => closed = true;
        Invoke(page, "Save_Click"); // A keyboard/programmatic attempt still validates.
        Assert.False(closed);
        Assert.Equal(0, writes);
        Assert.Equal(Visibility.Visible, Find<StackPanel>(page, "PanelRecording").Visibility);
        duration.Text = source.ReplayDurationSeconds.ToString();
        Assert.False(page.HasUnsavedChanges);
        Assert.Equal(Visibility.Collapsed, Find<TextBlock>(page, "TxtDurationError").Visibility);
        Assert.Contains("MiB", Find<TextBlock>(page, "TxtBufferWarning").Text);
        Assert.NotNull(Find<TextBlock>(page, "TxtBufferWarning").ToolTip);
        Assert.False(page.CanSaveDraft);
        var cap = Find<ComboBox>(page, "CmbShareCap");
        int originalCap = cap.SelectedIndex;
        cap.SelectedIndex = 4;
        Find<TextBox>(page, "TxtShareCustomMb").Text = "999";
        Assert.False(page.CanSaveDraft);
        Assert.Equal(Visibility.Visible, Find<TextBlock>(page, "TxtShareCapError").Visibility);
        cap.SelectedIndex = originalCap;
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.CanSaveDraft);
        Assert.Equal(0, endpointReads);
        Assert.Equal(0, startupReads);
    }

    private static void HiddenInvalidDraftReviewReturnsToField(string root)
    {
        var source = Preferences(root, "draft-review-route");
        int writes = 0;
        var page = new SettingsWindow(source, () => false,
            loadAudioEndpoints: _ => Array.Empty<AudioEndpoint>(),
            saveReviewedSettings: _ => writes++, applyWindowsStartup: (_, _) => { });
        var duration = Find<TextBox>(page, "TxtDuration");
        var review = Find<Button>(page, "BtnReviewSettings");
        Assert.Equal(Visibility.Collapsed, review.Visibility);
        duration.Text = "invalid draft";
        Find<Button>(page, "NavGeneral").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Visible, Find<StackPanel>(page, "PanelGeneral").Visibility);
        Assert.Equal(Visibility.Collapsed, Find<StackPanel>(page, "PanelRecording").Visibility);
        Assert.False(Find<Button>(page, "BtnSaveSettings").IsEnabled);
        Assert.Equal(Visibility.Visible, review.Visibility);
        Assert.True(review.IsEnabled);
        Assert.Equal("Review recording", review.Content);
        review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Visible, Find<StackPanel>(page, "PanelRecording").Visibility);
        Assert.Equal(Visibility.Collapsed, Find<StackPanel>(page, "PanelGeneral").Visibility);
        Assert.Same(duration, FocusManager.GetFocusedElement(FocusManager.GetFocusScope(duration)));
        Assert.Equal("invalid draft", duration.Text);
        Assert.Equal(Visibility.Visible, Find<TextBlock>(page, "TxtDurationError").Visibility);
        Assert.True(page.HasUnsavedChanges);
        Assert.False(page.CanSaveDraft);
        Assert.Equal(0, writes);
        Assert.Equal(30, source.ReplayDurationSeconds);
        duration.Text = source.ReplayDurationSeconds.ToString();
        Assert.Equal(Visibility.Collapsed, review.Visibility);
        Assert.False(page.HasUnsavedChanges);
    }

    private static void GameSelectionAndRevertedEdits(string root)
    {
        var source = Preferences(root, "draft-games");
        source.ApprovedGameProfiles.Add(new ApprovedGameProfile {
            Id = Guid.NewGuid(), Name = "Generated game", ExecutablePath = @"C:\Synthetic\game.exe",
            Enabled = true, QualityPreset = "Balanced", ReplayDurationSeconds = 30
        });
        var page = Page(source, Path.Combine(root, "draft-games.json"));
        Find<ComboBox>(page, "LstApprovedGames").SelectedIndex = 0;
        Assert.False(page.HasUnsavedChanges); // Viewing a profile is not an edit.
        var name = Find<TextBox>(page, "TxtGameName");
        name.Text = "Edited game";
        Assert.True(page.HasUnsavedChanges);
        Assert.True(page.CanSaveDraft);
        name.Text = "Generated game";
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.CanSaveDraft);
        Assert.Equal("Generated game", source.ApprovedGameProfiles[0].Name);

        // A blank name must return to the name field, rather than the valid
        // replay duration. The real footer button also works from another page.
        name.Text = "";
        Find<Button>(page, "NavGeneral").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var review = Find<Button>(page, "BtnReviewSettings");
        Assert.Equal("Review games", review.Content);
        Assert.False(Find<Button>(page, "BtnSaveSettings").IsEnabled);
        review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Visible, Find<StackPanel>(page, "PanelGames").Visibility);
        Assert.Equal(Visibility.Visible, Find<StackPanel>(page, "GameProfileEditor").Visibility);
        Assert.Same(name, FocusManager.GetFocusedElement(FocusManager.GetFocusScope(name)));
        Assert.Equal("", name.Text);
        Assert.Equal("Enter a game name.", Find<TextBlock>(page, "TxtGameNameError").Text);
        Assert.Equal(Visibility.Visible, Find<TextBlock>(page, "TxtGameNameError").Visibility);
        Assert.Equal("30", Find<TextBox>(page, "TxtGameDuration").Text);
        Assert.False(page.CanSaveDraft);
        Assert.Equal("Generated game", source.ApprovedGameProfiles[0].Name);

        name.Text = "Generated game";
        var duration = Find<TextBox>(page, "TxtGameDuration");
        duration.Text = "121";
        Find<Button>(page, "NavGeneral").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(duration, FocusManager.GetFocusedElement(FocusManager.GetFocusScope(duration)));
        Assert.Equal("121", duration.Text);
        Assert.Equal(Visibility.Visible, Find<TextBlock>(page, "TxtGameDurationError").Visibility);
        Assert.Contains("15–120", Find<TextBlock>(page, "TxtGameDurationError").Text);
        Assert.Equal(Visibility.Collapsed, Find<TextBlock>(page, "TxtGameNameError").Visibility);
        Assert.False(page.CanSaveDraft);
        Assert.Equal(30, source.ApprovedGameProfiles[0].ReplayDurationSeconds);
        duration.Text = "30";
        Assert.Equal(Visibility.Collapsed, review.Visibility);
        Assert.False(page.HasUnsavedChanges);
    }

    private static void ReviewedSaveReopenAndCancel(string root)
    {
        var source = Preferences(root, "draft-round-trip");
        string file = Path.Combine(root, "draft-round-trip.json");
        source.SaveReviewedToFile(file);
        var page = Page(source, file);
        Find<TextBox>(page, "TxtDuration").Text = "60";
        Find<CheckBox>(page, "ChkShowResourceUsage").IsChecked = !source.ShowResourceUsage;
        Assert.True(page.CanSaveDraft);
        bool verifiedBeforeClose = false;
        page.CloseRequested += saved => {
            Assert.True(saved);
            Assert.Equal(60, AppSettings.LoadExistingFile(file).ReplayDurationSeconds);
            verifiedBeforeClose = true;
        };
        Invoke(page, "Save_Click");
        Assert.True(verifiedBeforeClose);
        var persisted = AppSettings.LoadExistingFile(file);
        var reopened = Page(persisted, file);
        Assert.False(reopened.HasUnsavedChanges);
        Assert.False(reopened.CanSaveDraft);
        Assert.Equal("60", Find<TextBox>(reopened, "TxtDuration").Text);
        string before = File.ReadAllText(file);
        Find<TextBox>(reopened, "TxtDuration").Text = "90";
        bool cancelled = false;
        reopened.CloseRequested += saved => { Assert.False(saved); cancelled = true; };
        Invoke(reopened, "Cancel_Click");
        Assert.True(cancelled);
        Assert.Equal(60, persisted.ReplayDurationSeconds);
        Assert.Equal(before, File.ReadAllText(file));
    }

    private static void RepairAndRecoveredReviewRemainSaveable(string root)
    {
        var source = Preferences(root, "draft-startup");
        source.StartWithWindows = true;
        string file = Path.Combine(root, "draft-startup.json");
        bool repaired = false;
        int reads = 0;
        var page = new SettingsWindow(source, () => false,
            loadAudioEndpoints: _ => Array.Empty<AudioEndpoint>(),
            saveReviewedSettings: staged => staged.SaveReviewedToFile(file),
            applyWindowsStartup: (enabled, _) => { Assert.True(enabled); repaired = true; },
            readWindowsStartupStatus: _ => { reads++; return repaired
                ? new(WindowsStartupRegistrationState.Registered, "Registered")
                : new(WindowsStartupRegistrationState.MissingOrStale, "Save Settings to repair Windows startup."); });
        Assert.False(page.HasUnsavedChanges);
        Assert.True(page.CanSaveDraft);
        Assert.True(Find<Button>(page, "BtnSaveSettings").IsEnabled);
        Assert.Contains("repair", Find<TextBlock>(page, "TxtStartupStatus").Text);
        Find<TextBox>(page, "TxtDuration").Text = "invalid";
        Assert.False(page.CanSaveDraft);
        Find<TextBox>(page, "TxtDuration").Text = source.ReplayDurationSeconds.ToString();
        Assert.True(page.CanSaveDraft);
        Assert.Equal(1, reads); // Draft keystrokes must not query the OS.
        Invoke(page, "Save_Click");
        Assert.True(repaired);
        Assert.True(AppSettings.LoadExistingFile(file).StartWithWindows);
        Assert.False(Page(source, file).CanSaveDraft); // Registered + unchanged.

        string damaged = Path.Combine(root, "draft-recovered.json");
        File.WriteAllText(damaged, "{invalid-json");
        var recovered = AppSettings.LoadExistingFile(damaged);
        recovered.OriginalsFolder = Path.Combine(root, "draft-recovered-originals");
        recovered.ExportsFolder = Path.Combine(root, "draft-recovered-exports");
        var recoveryPage = Page(recovered, damaged);
        Assert.True(recovered.RequiresExplicitSettingsSave);
        Assert.False(recoveryPage.HasUnsavedChanges);
        Assert.True(recoveryPage.CanSaveDraft);
        Assert.Equal("{invalid-json", File.ReadAllText(damaged));
        Invoke(recoveryPage, "Save_Click");
        Assert.False(recovered.RequiresExplicitSettingsSave);
        Assert.False(AppSettings.LoadExistingFile(damaged).RequiresExplicitSettingsSave);
    }

    private static void DisconnectedDevicesPermitUnrelatedDrafts(string root)
    {
        var source = Preferences(root, "draft-offline");
        source.SystemAudioDeviceId = "remembered-output";
        source.MicrophoneDeviceId = "remembered-microphone";
        source.EnableMicrophone = false;
        var page = Page(source, Path.Combine(root, "draft-offline.json"));
        Find<CheckBox>(page, "ChkShowResourceUsage").IsChecked = !source.ShowResourceUsage;
        Assert.True(page.CanSaveDraft);
        Find<CheckBox>(page, "ChkMicrophone").IsChecked = true;
        Assert.False(page.CanSaveDraft); // New microphone opt-in needs an available selection.
        Assert.Equal(Visibility.Visible, Find<TextBlock>(page, "TxtMicrophoneError").Visibility);
        Find<CheckBox>(page, "ChkMicrophone").IsChecked = false;
        Assert.True(page.CanSaveDraft);
        Assert.Equal(Visibility.Collapsed, Find<TextBlock>(page, "TxtMicrophoneError").Visibility);
    }

    private static AppSettings Preferences(string root, string name)
    {
        var settings = AppSettings.CreateDefault();
        settings.OriginalsFolder = Path.Combine(root, name + "-originals");
        settings.ExportsFolder = Path.Combine(root, name + "-exports");
        settings.StartWithWindows = false;
        return settings;
    }

    private static SettingsWindow Page(AppSettings source, string file) => new(source, () => false,
        loadAudioEndpoints: _ => Array.Empty<AudioEndpoint>(),
        saveReviewedSettings: staged => staged.SaveReviewedToFile(file),
        applyWindowsStartup: (_, _) => { },
        readWindowsStartupStatus: _ => new(WindowsStartupRegistrationState.Registered, "Registered"));
    private static T Find<T>(SettingsWindow page, string name) => Assert.IsType<T>(page.FindName(name));
    private static void Invoke(SettingsWindow page, string method) => typeof(SettingsWindow)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(page, new object[] { page, new RoutedEventArgs() });
}
