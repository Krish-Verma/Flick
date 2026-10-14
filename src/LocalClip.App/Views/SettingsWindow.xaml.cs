using System;
using System.IO;
using System.Windows;
using LocalClip.Core.Models;
using LocalClip.Core.Sharing;

namespace LocalClip.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        TxtToggleHotkey.Text = _settings.CaptureToggleHotkey;
        TxtSaveHotkey.Text = _settings.SaveReplayHotkey;
        TxtOriginalsFolder.Text = _settings.OriginalsFolder;
        TxtDuration.Text = _settings.ReplayDurationSeconds.ToString();
        TxtWebhookChannel.Text = _settings.DiscordWebhookChannelName;

        string plainUrl = DiscordWebhookClient.UnprotectSecret(_settings.MaskedWebhookUrl);
        TxtWebhookUrl.Password = plainUrl;
    }

    private void BrowseOriginals_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "Select Folder for Saved Clips";
        dialog.UseDescriptionForTitle = true;
        dialog.SelectedPath = _settings.OriginalsFolder;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtOriginalsFolder.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtDuration.Text, out int dur))
        {
            _settings.ReplayDurationSeconds = Math.Clamp(dur, 15, 120);
        }

        _settings.CaptureToggleHotkey = TxtToggleHotkey.Text.Trim();
        _settings.SaveReplayHotkey = TxtSaveHotkey.Text.Trim();
        _settings.OriginalsFolder = TxtOriginalsFolder.Text.Trim();
        _settings.DiscordWebhookChannelName = TxtWebhookChannel.Text.Trim();

        string webhookUrl = TxtWebhookUrl.Password.Trim();
        if (!string.IsNullOrEmpty(webhookUrl))
        {
            if (DiscordWebhookClient.ValidateWebhookUrl(webhookUrl, out string err))
            {
                _settings.MaskedWebhookUrl = DiscordWebhookClient.ProtectSecret(webhookUrl);
                _settings.EnableDiscordWebhook = true;
            }
            else
            {
                MessageBox.Show(err, "Invalid Discord Webhook", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            _settings.MaskedWebhookUrl = "";
            _settings.EnableDiscordWebhook = false;
        }

        _settings.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show("Restore settings to factory defaults? Your existing clip recordings will NOT be deleted.", "Restore Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
        {
            var def = AppSettings.CreateDefault();
            TxtToggleHotkey.Text = def.CaptureToggleHotkey;
            TxtSaveHotkey.Text = def.SaveReplayHotkey;
            TxtOriginalsFolder.Text = def.OriginalsFolder;
            TxtDuration.Text = def.ReplayDurationSeconds.ToString();
            TxtWebhookChannel.Text = "";
            TxtWebhookUrl.Password = "";
        }
    }
}
