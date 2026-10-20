using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalClip.App.Hotkeys;
using LocalClip.App.Services;
using LocalClip.Core.Models;
using LocalClip.Core.Sharing;

namespace LocalClip.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    // Only an explicit preview opens an overlay before settings are saved.
    public event Action<bool, bool>? PreviewNotificationRequested;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Populate(settings);
    }

    private void Populate(AppSettings s)
    {
        TxtToggleHotkey.Text = s.CaptureToggleHotkey;
        TxtSaveHotkey.Text = s.SaveReplayHotkey;
        TxtOriginalsFolder.Text = s.OriginalsFolder;
        TxtDuration.Text = s.ReplayDurationSeconds.ToString();
        CmbQuality.SelectedIndex = RecordingProfiles.Identify(s) switch
        {
            RecordingProfiles.Light => 0,
            RecordingProfiles.Smooth => 2,
            _ => 1
        };
        ChkMicrophone.IsChecked = s.EnableMicrophone;
        ChkStartWithWindows.IsChecked = s.StartWithWindows;
        ChkStartMinimized.IsChecked = s.StartMinimizedToTray;
        ChkClipSavedOverlay.IsChecked = s.ShowClipSavedOverlay;
        ChkClipSavedSound.IsChecked = s.PlayClipSavedSound;
        TxtWebhookChannel.Text = s.DiscordWebhookChannelName;
        TxtWebhookUrl.Password = DiscordWebhookClient.UnprotectSecret(s.MaskedWebhookUrl);
        UpdateBufferWarning();
    }

    private void CaptureProfile_Changed(object sender, EventArgs e) => UpdateBufferWarning();

    private void UpdateBufferWarning()
    {
        if (TxtBufferWarning == null || CmbQuality == null || TxtDuration == null) return;
        if (!int.TryParse(TxtDuration.Text, out int duration) || duration < 15 || duration > 120)
        {
            TxtBufferWarning.Text = "Choose a replay duration from 15 to 120 seconds.";
            return;
        }
        var profile = AppSettings.CreateDefault();
        if (!RecordingProfiles.TryApply(Preset(), profile)) return;
        var estimate = RecordingProfiles.EstimatedReplayBytes(duration, profile.VideoBitrateKbps,
            ChkMicrophone?.IsChecked == true) / 1048576d;
        TxtBufferWarning.Text = estimate > profile.BufferMemoryLimitMb
            ? $"Estimated compressed replay is {estimate:F0} MiB, above the {profile.BufferMemoryLimitMb} MiB packet cap. The retained clip may be shorter."
            : $"Estimated compressed replay: ~{estimate:F0} MiB of {profile.BufferMemoryLimitMb} MiB maximum. Higher quality uses more resources.";
    }

    private string Preset() => CmbQuality.SelectedIndex switch
    {
        0 => RecordingProfiles.Light,
        2 => RecordingProfiles.Smooth,
        _ => RecordingProfiles.Balanced
    };

    private void BrowseOriginals_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose where Flick stores original clips",
            UseDescriptionForTitle = true,
            SelectedPath = _settings.OriginalsFolder
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtOriginalsFolder.Text = dialog.SelectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtDuration.Text, out int duration) || duration < 15 || duration > 120)
        {
            MessageBox.Show("Replay duration must be between 15 and 120 seconds.", "Invalid duration");
            return;
        }
        var toggle = TxtToggleHotkey.Text.Trim();
        var save = TxtSaveHotkey.Text.Trim();
        if (!HotkeyManager.IsValidHotkey(toggle) || !HotkeyManager.IsValidHotkey(save) ||
            HotkeyManager.AreEquivalentHotkeys(toggle, save))
        {
            MessageBox.Show("Choose two distinct valid hotkeys, such as Ctrl+Shift+F9 and Ctrl+Shift+F8.", "Invalid shortcuts");
            return;
        }
        var folder = TxtOriginalsFolder.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            MessageBox.Show("Choose a full path for saved clips.", "Invalid folder");
            return;
        }
        var hook = TxtWebhookUrl.Password.Trim();
        if (!string.IsNullOrEmpty(hook) && !DiscordWebhookClient.ValidateWebhookUrl(hook, out var error))
        {
            MessageBox.Show(error, "Invalid webhook URL");
            return;
        }

        try
        {
            // Windows startup registration is explicit; failure must not silently claim success.
            WindowsStartupRegistration.Apply(ChkStartWithWindows.IsChecked == true, ChkStartMinimized.IsChecked == true);
            _settings.CaptureToggleHotkey = toggle;
            _settings.SaveReplayHotkey = save;
            _settings.OriginalsFolder = folder;
            _settings.ReplayDurationSeconds = duration;
            _settings.EnableMicrophone = ChkMicrophone.IsChecked == true;
            _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
            _settings.StartMinimizedToTray = ChkStartMinimized.IsChecked == true;
            _settings.ShowClipSavedOverlay = ChkClipSavedOverlay.IsChecked == true;
            _settings.PlayClipSavedSound = ChkClipSavedSound.IsChecked == true;
            RecordingProfiles.TryApply(Preset(), _settings);
            _settings.DiscordWebhookChannelName = TxtWebhookChannel.Text.Trim();
            _settings.MaskedWebhookUrl = string.IsNullOrEmpty(hook)
                ? "" : DiscordWebhookClient.ProtectSecret(hook);
            _settings.EnableDiscordWebhook = !string.IsNullOrEmpty(hook);
            _settings.Save();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Settings could not be saved: " + ex.Message, "Flick settings");
        }
    }

    private void PreviewNotification_Click(object sender, RoutedEventArgs e)
    {
        PreviewNotificationRequested?.Invoke(true, ChkClipSavedSound.IsChecked == true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Restore defaults? Your saved clips will not be deleted.",
            "Restore Flick defaults", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
            MessageBoxResult.Yes)
            Populate(AppSettings.CreateDefault());
    }
}
