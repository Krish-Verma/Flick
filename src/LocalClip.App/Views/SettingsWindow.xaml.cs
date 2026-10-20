using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalClip.App.Hotkeys;
using LocalClip.App.Services;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Sharing;

namespace LocalClip.App.Views;

/// <summary>
/// One compact, six-category settings surface. All changes are staged until
/// Save; Preview Notification and Manage Folders are explicit separate actions.
/// Recording stays armed while simply browsing Settings.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly bool _captureActive;
    private bool _loading;
    private string _activeCategory = "Recording";

    public event Action<bool, bool>? PreviewNotificationRequested;
    public event EventHandler? ManageFoldersRequested;

    public SettingsWindow(AppSettings settings, bool captureActive = false)
    {
        InitializeComponent();
        _settings = settings;
        _captureActive = captureActive;
        Populate(settings);
        SelectCategory("Recording");
    }

    private void SettingsCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string category })
            SelectCategory(category);
    }

    private void SelectCategory(string category)
    {
        _activeCategory = category;
        foreach (var (button, panel, name) in new[]
        {
            (NavRecording, PanelRecording, "Recording"),
            (NavAudio, PanelAudio, "Audio"),
            (NavShortcuts, PanelShortcuts, "Shortcuts"),
            (NavStorage, PanelStorage, "Storage"),
            (NavGeneral, PanelGeneral, "General"),
            (NavSharing, PanelSharing, "Sharing")
        })
        {
            bool selected = category == name;
            panel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            button.Background = selected
                ? (System.Windows.Media.Brush)FindResource("Flick.Brush.Frosted")
                : System.Windows.Media.Brushes.Transparent;
            button.Foreground = (System.Windows.Media.Brush)FindResource(
                selected ? "Flick.Brush.TextPrimary" : "Flick.Brush.TextSecondary");
        }
        SettingsScroll.ScrollToTop();
    }

    private void Populate(AppSettings settings)
    {
        _loading = true;
        try
        {
            TxtToggleHotkey.Text = settings.CaptureToggleHotkey;
            TxtSaveHotkey.Text = settings.SaveReplayHotkey;
            TxtOriginalsFolder.Text = settings.OriginalsFolder;
            TxtExportsFolder.Text = settings.ExportsFolder;
            TxtDuration.Text = settings.ReplayDurationSeconds.ToString();
            CmbQuality.SelectedIndex = RecordingProfiles.Identify(settings) switch
            {
                RecordingProfiles.Light => 0,
                RecordingProfiles.Smooth => 2,
                _ => 1
            };
            ChkMicrophone.IsChecked = settings.EnableMicrophone;
            ChkStartWithWindows.IsChecked = settings.StartWithWindows;
            ChkStartMinimized.IsChecked = settings.StartMinimizedToTray;
            ChkClipSavedOverlay.IsChecked = settings.ShowClipSavedOverlay;
            ChkClipSavedSound.IsChecked = settings.PlayClipSavedSound;
            ChkEnableWebhook.IsChecked = settings.EnableDiscordWebhook;
            TxtWebhookChannel.Text = settings.DiscordWebhookChannelName;
            TxtWebhookUrl.Password = DiscordWebhookClient.UnprotectSecret(settings.MaskedWebhookUrl);
            int cap = ShareExportTargets.IsValid(settings.DiscordTargetSizeMb)
                ? settings.DiscordTargetSizeMb : ShareExportTargets.DefaultMb;
            CmbShareCap.SelectedIndex = cap switch
            {
                10 => 0, 20 => 1, 50 => 2, 100 => 3, _ => 4
            };
            TxtShareCustomMb.Text = cap.ToString();
            ShareCustomPanel.Visibility = CmbShareCap.SelectedIndex == 4
                ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _loading = false; }
        UpdateBufferWarning();
    }

    private string Preset() => CmbQuality.SelectedIndex switch
    {
        0 => RecordingProfiles.Light,
        2 => RecordingProfiles.Smooth,
        _ => RecordingProfiles.Balanced
    };

    private void CaptureProfile_Changed(object sender, EventArgs e)
    {
        if (!_loading) UpdateBufferWarning();
    }

    private void Microphone_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) UpdateBufferWarning();
    }

    private void UpdateBufferWarning()
    {
        if (TxtBufferWarning is null || CmbQuality is null || TxtDuration is null)
            return;
        if (!int.TryParse(TxtDuration.Text, out int duration) ||
            duration < 15 || duration > 120)
        {
            TxtBufferWarning.Text = "Choose a Replay Duration Between 15 and 120 Seconds.";
            return;
        }
        var target = AppSettings.CreateDefault();
        if (!RecordingProfiles.TryApply(Preset(), target)) return;
        double estimate = RecordingProfiles.EstimatedReplayBytes(
            duration, target.VideoBitrateKbps, ChkMicrophone?.IsChecked == true) / 1_048_576d;
        TxtBufferWarning.Text = estimate > target.BufferMemoryLimitMb
            ? $"Estimated Replay: ~{estimate:F0} MiB exceeds {target.BufferMemoryLimitMb} MiB buffer. The retained clip may be shorter."
            : $"Estimated Replay: ~{estimate:F0} MiB of {target.BufferMemoryLimitMb} MiB compressed packet budget.";
    }

    private void ShareCap_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ShareCustomPanel is null || CmbShareCap is null) return;
        ShareCustomPanel.Visibility = CmbShareCap.SelectedIndex == 4
            ? Visibility.Visible : Visibility.Collapsed;
        if (CmbShareCap.SelectedIndex == 4 && string.IsNullOrWhiteSpace(TxtShareCustomMb.Text))
            TxtShareCustomMb.Text = _settings.DiscordTargetSizeMb.ToString();
    }

    private int GetShareCap()
    {
        return CmbShareCap.SelectedIndex switch
        {
            0 => 10,
            1 => 20,
            2 => 50,
            3 => 100,
            4 when int.TryParse(TxtShareCustomMb.Text, out int custom) => custom,
            _ => 0
        };
    }

    private void Shortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (e.Key == Key.Tab) return;
        if (e.Key == Key.Escape)
        {
            textBox.Text = ReferenceEquals(textBox, TxtToggleHotkey)
                ? _settings.CaptureToggleHotkey : _settings.SaveReplayHotkey;
            e.Handled = true;
            return;
        }
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) == 0)
            return; // Avoid unexpected global shortcuts for normal typed characters.

        string prefix = "";
        if (modifiers.HasFlag(ModifierKeys.Control)) prefix += "Ctrl+";
        if (modifiers.HasFlag(ModifierKeys.Shift)) prefix += "Shift+";
        if (modifiers.HasFlag(ModifierKeys.Alt)) prefix += "Alt+";
        if (modifiers.HasFlag(ModifierKeys.Windows)) prefix += "Win+";
        string proposal = prefix + key;
        if (HotkeyManager.IsValidHotkey(proposal))
            textBox.Text = proposal;
        e.Handled = true;
    }

    private void BrowseOriginals_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Where Flick Saves New Original Clips",
            UseDescriptionForTitle = true,
            SelectedPath = TxtOriginalsFolder.Text
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtOriginalsFolder.Text = dialog.SelectedPath;
    }

    private void BrowseExports_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Where Flick Saves Share-Ready MP4s",
            UseDescriptionForTitle = true,
            SelectedPath = TxtExportsFolder.Text
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtExportsFolder.Text = dialog.SelectedPath;
    }

    private void ManageFolders_Click(object sender, RoutedEventArgs e) =>
        ManageFoldersRequested?.Invoke(this, EventArgs.Empty);

    private void PreviewNotification_Click(object sender, RoutedEventArgs e) =>
        PreviewNotificationRequested?.Invoke(true, ChkClipSavedSound.IsChecked == true);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtDuration.Text, out int duration) ||
            duration < 15 || duration > 120)
        {
            SelectCategory("Recording");
            MessageBox.Show("Replay Duration Must Be Between 15 and 120 Seconds.", "Invalid Duration");
            return;
        }
        int cap = GetShareCap();
        if (!ShareExportTargets.IsValid(cap))
        {
            SelectCategory("Sharing");
            MessageBox.Show("Select a File Limit Between 1 and 250 MB.", "Invalid Export Limit");
            return;
        }
        string toggle = TxtToggleHotkey.Text.Trim();
        string save = TxtSaveHotkey.Text.Trim();
        if (!HotkeyManager.IsValidHotkey(toggle) || !HotkeyManager.IsValidHotkey(save) ||
            HotkeyManager.AreEquivalentHotkeys(toggle, save))
        {
            SelectCategory("Shortcuts");
            MessageBox.Show("Choose Two Different Valid Hotkeys.", "Invalid Shortcuts");
            return;
        }
        string originals, exports;
        try
        {
            originals = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(TxtOriginalsFolder.Text.Trim()));
            exports = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(TxtExportsFolder.Text.Trim()));
            if (!Path.IsPathFullyQualified(originals) || !Path.IsPathFullyQualified(exports) ||
                string.Equals(originals, exports, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Saved Clips and Exports must be separate, fully qualified folders.");
        }
        catch (Exception ex)
        {
            SelectCategory("Storage");
            MessageBox.Show("Invalid Storage Paths: " + ex.Message, "Storage Settings");
            return;
        }

        string webhook = TxtWebhookUrl.Password.Trim();
        bool enableWebhook = ChkEnableWebhook.IsChecked == true;
        if (enableWebhook && (string.IsNullOrWhiteSpace(webhook) ||
            !DiscordWebhookClient.ValidateWebhookUrl(webhook, out _)))
        {
            SelectCategory("Sharing");
            MessageBox.Show("A valid Discord webhook is required to enable optional direct posting.",
                "Invalid Webhook");
            return;
        }
        if (_captureActive && _settings.EnableMicrophone && ChkMicrophone.IsChecked != true &&
            MessageBox.Show("Turning off an active microphone will stop the replay buffer and discard unsaved footage. Continue?",
                "Disable Microphone", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        bool originalStartup = _settings.StartWithWindows;
        bool originalMinimized = _settings.StartMinimizedToTray;
        bool startupTouched = false;
        try
        {
            // Stage all edits in a temporary snapshot: cancelling or an invalid
            // field never partially rewrites the live VM's settings.
            var staged = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings)) ??
                throw new InvalidOperationException("Could not stage current settings.");
            staged.CaptureToggleHotkey = toggle;
            staged.SaveReplayHotkey = save;
            staged.OriginalsFolder = originals;
            staged.ExportsFolder = exports;
            staged.ReplayDurationSeconds = duration;
            staged.EnableMicrophone = ChkMicrophone.IsChecked == true;
            staged.StartWithWindows = ChkStartWithWindows.IsChecked == true;
            staged.StartMinimizedToTray = ChkStartMinimized.IsChecked == true;
            staged.ShowClipSavedOverlay = ChkClipSavedOverlay.IsChecked == true;
            staged.PlayClipSavedSound = ChkClipSavedSound.IsChecked == true;
            staged.DiscordTargetSizeMb = cap;
            RecordingProfiles.TryApply(Preset(), staged);
            staged.DiscordWebhookChannelName = TxtWebhookChannel.Text.Trim();
            staged.MaskedWebhookUrl = string.IsNullOrWhiteSpace(webhook)
                ? "" : DiscordWebhookClient.ProtectSecret(webhook);
            staged.EnableDiscordWebhook = enableWebhook && webhook.Length > 0;

            Directory.CreateDirectory(originals);
            Directory.CreateDirectory(exports);
            WindowsStartupRegistration.Apply(staged.StartWithWindows, staged.StartMinimizedToTray);
            startupTouched = true;
            staged.Save();

            // Commit only GUI-managed properties after the atomic disk write.
            _settings.CaptureToggleHotkey = staged.CaptureToggleHotkey;
            _settings.SaveReplayHotkey = staged.SaveReplayHotkey;
            _settings.OriginalsFolder = staged.OriginalsFolder;
            _settings.ExportsFolder = staged.ExportsFolder;
            _settings.ReplayDurationSeconds = staged.ReplayDurationSeconds;
            _settings.VideoWidth = staged.VideoWidth;
            _settings.VideoHeight = staged.VideoHeight;
            _settings.VideoFps = staged.VideoFps;
            _settings.VideoBitrateKbps = staged.VideoBitrateKbps;
            _settings.VideoEncoder = staged.VideoEncoder;
            _settings.QualityPreset = staged.QualityPreset;
            _settings.EnableMicrophone = staged.EnableMicrophone;
            _settings.StartWithWindows = staged.StartWithWindows;
            _settings.StartMinimizedToTray = staged.StartMinimizedToTray;
            _settings.ShowClipSavedOverlay = staged.ShowClipSavedOverlay;
            _settings.PlayClipSavedSound = staged.PlayClipSavedSound;
            _settings.DiscordTargetSizeMb = staged.DiscordTargetSizeMb;
            _settings.DiscordWebhookChannelName = staged.DiscordWebhookChannelName;
            _settings.MaskedWebhookUrl = staged.MaskedWebhookUrl;
            _settings.EnableDiscordWebhook = staged.EnableDiscordWebhook;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            if (startupTouched)
            {
                try { WindowsStartupRegistration.Apply(originalStartup, originalMinimized); }
                catch { /* Warn: the original registry entry may need manual review. */ }
            }
            MessageBox.Show("Settings Couldn't Be Saved: " + ex.Message, "Flick Settings");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Restore the default settings? Existing clips and exported files will not be deleted.",
            "Restore Flick Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
            MessageBoxResult.Yes)
            Populate(AppSettings.CreateDefault()); // Still staged; Save applies it.
    }
}
