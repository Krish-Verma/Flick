using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalClip.App.Hotkeys;
using LocalClip.App.Services;
using LocalClip.Core.Audio;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Sharing;
using LocalClip.Core.Updates;

namespace LocalClip.App.Views;

/// <summary>
/// One compact, six-category settings surface. All changes are staged until
/// Save; Preview Notification and Manage Folders are explicit separate actions.
/// Recording stays armed while simply browsing Settings.
/// </summary>
public partial class SettingsWindow : UserControl
{
    private readonly AppSettings _settings;
    // The recording bar remains interactive while Settings is open; query
    // live capture status at Save time instead of using a stale snapshot.
    private readonly Func<bool> _isCaptureActive;
    // Injected only by isolated WPF tests; production uses the verified
    // actual-profile write and per-user Windows startup registration.
    private readonly Action<AppSettings> _saveReviewedSettings;
    private readonly Action<bool, bool> _applyWindowsStartup;
    public string LastSaveWarning { get; private set; } = "";
    private string _customSoundPath = "";
    private bool _loading;
    private TextBox? _editingShortcut;
    private string _shortcutCandidate = "";
    private bool _recordingShortcut;
    public event Action<bool>? ShortcutEditingChanged;
    private string _activeCategory = "Recording";

    private CancellationTokenSource? _updateCancellation;
    private AvailableUpdate? _availableUpdate;
    private DownloadedUpdate? _downloadedUpdate;
    private bool _installingUpdate;
    private bool _installerStarted;
    private CancellationTokenSource? _installationCancellation;
    public event Func<DownloadedUpdate, CancellationToken, Task<string?>>? InstallUpdateRequested;

    public bool UninstallRequested { get; private set; }

    /// <summary>True only after a successful, deliberate Save.</summary>
    public event Action<bool>? CloseRequested;
    public event Action<bool, bool, string, string>? PreviewNotificationRequested;
    public event EventHandler? ManageFoldersRequested;

    public SettingsWindow(AppSettings settings, Func<bool> isCaptureActive, Func<Task<TargetList>>? loadAudioSources = null,
        Func<AudioEndpointFlow, IReadOnlyList<AudioEndpoint>>? loadAudioEndpoints = null,
        Action<AppSettings>? saveReviewedSettings = null, Action<bool, bool>? applyWindowsStartup = null)
    {
        InitializeComponent();
        _settings = settings;
        _isCaptureActive = isCaptureActive ?? throw new ArgumentNullException(nameof(isCaptureActive));
        _loadAudioSources = loadAudioSources;
        _loadAudioEndpoints = loadAudioEndpoints ?? WindowsAudioEndpointEnumerator.Enumerate;
        _saveReviewedSettings = saveReviewedSettings ??
            (reviewed => reviewed.SaveReviewedToFile(AppSettings.GetSettingsFilePath()));
        _applyWindowsStartup = applyWindowsStartup ?? WindowsStartupRegistration.Apply;
        Populate(settings);
        SelectCategory("Recording");
        TxtCurrentVersion.Text = "Current version: " + FlickUpdateService.CurrentVersion;
        ChkPreviewUpdates.IsChecked = settings.IncludePreviewUpdates;
        TxtUpdateMode.Text = FlickUninstallLauncher.IsInstalled
            ? "Updates keep your clips and saved preferences. Save settings before installing; pending changes on this page are not applied by an update."
            : "Portable copy: open the release page for a new ZIP, then quit Flick and replace the extracted application folder. Your clips and preferences live separately.";
        Unloaded += (_, _) => LeavePage();
    }

    /// <summary>Leaving Settings cancels network work and removes this page's staged installer.</summary>
    public void LeavePage()
    {
        // Discard an in-flight read-only endpoint enumeration when leaving.
        _audioDevicesEpoch++;
        _gameApprovalCancellation?.Cancel();
        _updateCancellation?.Cancel();
        _installationCancellation?.Cancel();
        _updateCancellation = null;
        DiscardDownloadedUpdate();
        if (!_installingUpdate)
        {
            SetUpdateBusy(false);
            TxtUpdateStatus.Text = "Check for updates when you're ready.";
        }
    }

    /// <summary>The child setup process owns its downloaded executable until it has finished.</summary>
    public void MarkUpdateInstallerStarted()
    {
        _installerStarted = true;
        _downloadedUpdate = null; // Do not delete a setup file while its child process is starting.
    }

    private void DiscardDownloadedUpdate()
    {
        if (_downloadedUpdate != null)
            FlickUpdateService.DiscardDownload(_downloadedUpdate);
        _downloadedUpdate = null;
    }

    private void SetUpdateBusy(bool busy)
    {
        BtnCheckUpdates.IsEnabled = !busy;
        ChkPreviewUpdates.IsEnabled = !busy;
        BtnDownloadUpdate.Visibility = !busy && _availableUpdate != null && _downloadedUpdate == null
            && FlickUninstallLauncher.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        BtnInstallUpdate.Visibility = !busy && _downloadedUpdate != null ? Visibility.Visible : Visibility.Collapsed;
        BtnCancelUpdate.Visibility = busy && !_installingUpdate ? Visibility.Visible : Visibility.Collapsed;
        BtnCancelUpdate.IsEnabled = true;
        UpdateProgress.Visibility = busy && !_installingUpdate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PreviewUpdates_Changed(object sender, RoutedEventArgs e)
    {
        if (TxtUpdateStatus == null || _updateCancellation != null) return;
        _availableUpdate = null;
        DiscardDownloadedUpdate();
        TxtUpdateStatus.Text = "Check for updates to use this release channel.";
        SetUpdateBusy(false);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_updateCancellation != null || _installingUpdate) return;
        DiscardDownloadedUpdate();
        _availableUpdate = null;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetUpdateBusy(true);
        UpdateProgress.IsIndeterminate = true;
        TxtUpdateStatus.Text = "Checking GitHub Releases…";
        try
        {
            var client = new UpdateClient();
            var release = await client.CheckAsync(FlickUpdateService.CurrentVersion,
                ChkPreviewUpdates.IsChecked == true, cancellation.Token);
            if (!ReferenceEquals(_updateCancellation, cancellation)) return;
            _availableUpdate = release;
            TxtUpdateStatus.Text = release == null ? "You're up to date on this release channel."
                : $"Flick {release.Version} is available." + (FlickUninstallLauncher.IsInstalled
                    ? " Review the release notes, then download when you're ready."
                    : " Open Release Notes to download the portable ZIP.");
        }
        catch (OperationCanceledException)
        { if (ReferenceEquals(_updateCancellation, cancellation)) TxtUpdateStatus.Text = "Update check cancelled."; }
        catch (Exception)
        { if (ReferenceEquals(_updateCancellation, cancellation)) TxtUpdateStatus.Text = "Couldn't check for updates. Check your connection and try again, or open Release Notes."; }
        finally
        {
            if (ReferenceEquals(_updateCancellation, cancellation))
            { _updateCancellation = null; SetUpdateBusy(false); }
        }
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate == null || _updateCancellation != null || _installingUpdate) return;
        if (!FlickUninstallLauncher.IsInstalled)
        { UpdateReleaseNotes_Click(sender, e); return; }
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetUpdateBusy(true);
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;
        TxtUpdateStatus.Text = "Downloading update…";
        var progress = new Progress<double>(percent =>
        {
            if (ReferenceEquals(_updateCancellation, cancellation))
                UpdateProgress.Value = Math.Clamp(percent * 100, 0, 100);
        });
        try
        {
            var client = new UpdateClient();
            var download = await client.DownloadAsync(_availableUpdate,
                FlickUpdateService.DownloadDirectory, progress, cancellation.Token);
            if (!ReferenceEquals(_updateCancellation, cancellation))
            { FlickUpdateService.DiscardDownload(download); return; }
            _downloadedUpdate = download;
            TxtUpdateStatus.Text = $"Flick {download.Release.Version} downloaded and checksum verified. Save settings, then choose Install and Restart when replay is off and other work has finished.";
        }
        catch (OperationCanceledException)
        { if (ReferenceEquals(_updateCancellation, cancellation)) TxtUpdateStatus.Text = "Download cancelled. Your installed version is unchanged."; }
        catch (Exception)
        { if (ReferenceEquals(_updateCancellation, cancellation)) TxtUpdateStatus.Text = "The update couldn't be downloaded or verified. Try again, or open Release Notes."; }
        finally
        {
            if (ReferenceEquals(_updateCancellation, cancellation))
            { _updateCancellation = null; SetUpdateBusy(false); }
        }
    }

    private void CancelUpdate_Click(object sender, RoutedEventArgs e)
    {
        _updateCancellation?.Cancel();
        BtnCancelUpdate.IsEnabled = false;
        TxtUpdateStatus.Text = "Cancelling…";
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadedUpdate == null || _installingUpdate || _updateCancellation != null ||
            InstallUpdateRequested == null) return;
        var download = _downloadedUpdate;
        using var cancellation = new CancellationTokenSource();
        _installationCancellation = cancellation;
        _installingUpdate = true;
        SetUpdateBusy(true);
        TxtUpdateStatus.Text = "Checking installer and Windows signature…";
        try
        {
            string? message = await InstallUpdateRequested.Invoke(download, cancellation.Token);
            if (message != null) TxtUpdateStatus.Text = message;
        }
        catch (Exception)
        { TxtUpdateStatus.Text = "Flick couldn't start the installer. Your installed version is unchanged."; }
        finally
        {
            if (cancellation.IsCancellationRequested && !_installerStarted) FlickUpdateService.DiscardDownload(download);
            _installationCancellation = null;
            _installingUpdate = false;
            SetUpdateBusy(false);
        }
    }

    private void UpdateReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        try { FlickUpdateService.OpenReleasePage(_availableUpdate?.ReleaseUrl); }
        catch (Exception) { TxtUpdateStatus.Text = "Couldn't open GitHub Releases. Visit github.com/Krish-Verma/Flick/releases."; }
    }

    private void SettingsCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string category })
            SelectCategory(category);
    }

    private void SelectCategory(string category)
    {
        CancelShortcutEdit();
        _activeCategory = category;
        foreach (var (button, panel, name) in new[]
        {
            (NavRecording, PanelRecording, "Recording"),
            (NavAudio, PanelAudio, "Audio"),
            (NavGames, PanelGames, "Games"),
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
            PopulateGameProfiles(settings);
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
            PopulateAudio(settings);
            ChkMicrophone.IsChecked = settings.EnableMicrophone;
            ChkSoftwareFallback.IsChecked = settings.AllowSoftwareFallback;
            ChkStartWithWindows.IsChecked = settings.StartWithWindows;
            ChkStartMinimized.IsChecked = settings.StartMinimizedToTray;
            ChkShowResourceUsage.IsChecked = settings.ShowResourceUsage;
            ChkPreviewUpdates.IsChecked = settings.IncludePreviewUpdates;
            ChkClipSavedOverlay.IsChecked = settings.ShowClipSavedOverlay;
            ChkClipSavedSound.IsChecked = settings.PlayClipSavedSound;
            CmbNotificationTone.SelectedIndex = settings.ClipSavedSoundTone switch { "Quiet" => 1, "Custom" => 2, _ => 0 };
            _customSoundPath = settings.ClipSavedSoundPath;
            CustomSoundName.Text = string.IsNullOrWhiteSpace(_customSoundPath) ? "No custom sound selected" : Path.GetFileName(_customSoundPath);
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
        SaveShortcutLabel.Text = $"Save Last {settings.ReplayDurationSeconds}s";
    }

    private string Preset() => CmbQuality.SelectedIndex switch
    {
        0 => RecordingProfiles.Light,
        2 => RecordingProfiles.Smooth,
        _ => RecordingProfiles.Balanced
    };

    private void StageDisplay(AppSettings staged) =>
        staged.ShowResourceUsage = ChkShowResourceUsage.IsChecked == true;

    private void CaptureProfile_Changed(object sender, EventArgs e)
    {
        if (!_loading)
        {
            UpdateBufferWarning();
            if (SaveShortcutLabel != null && int.TryParse(TxtDuration.Text, out int seconds))
                SaveShortcutLabel.Text = $"Save Last {seconds}s";
        }
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

    private void ChangeShortcut_Click(object sender, RoutedEventArgs e)
    {
        CancelShortcutEdit();
        _editingShortcut = sender is Button { Tag: "Toggle" } ? TxtToggleHotkey : TxtSaveHotkey;
        ShortcutEditor.Visibility = Visibility.Visible;
        ShortcutHint.Text = "Select Record Shortcut, then press a combination with Ctrl or Alt.";
        ShortcutEditingChanged?.Invoke(true);
    }

    private void RecordShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_editingShortcut is null) return;
        _recordingShortcut = true;
        _shortcutCandidate = "";
        SaveShortcutButton.IsEnabled = false;
        ShortcutHint.Text = "Listening here… press a full shortcut. Escape cancels.";
        _editingShortcut.Focus();
    }

    private void CancelShortcutEdit()
    {
        _editingShortcut = null;
        _recordingShortcut = false;
        _shortcutCandidate = "";
        if (ShortcutEditor != null) ShortcutEditor.Visibility = Visibility.Collapsed;
        if (SaveShortcutButton != null) SaveShortcutButton.IsEnabled = false;
        ShortcutEditingChanged?.Invoke(false);
    }

    private void CancelShortcut_Click(object sender, RoutedEventArgs e) => CancelShortcutEdit();

    private void SaveShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_editingShortcut is null || !SaveShortcutButton.IsEnabled) return;
        _editingShortcut.Text = _shortcutCandidate; // staged only
        CancelShortcutEdit();
    }

    private void Shortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editingShortcut is null || !_recordingShortcut ||
            !ReferenceEquals(sender, _editingShortcut)) return;
        if (e.Key == Key.Tab) { _recordingShortcut = false; return; }
        e.Handled = true;
        if (e.Key == Key.Escape) { CancelShortcutEdit(); return; }
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None) return;
        ModifierKeys modifiers = Keyboard.Modifiers;
        string proposal = "";
        if (modifiers.HasFlag(ModifierKeys.Control)) proposal += "Ctrl+";
        if (modifiers.HasFlag(ModifierKeys.Shift)) proposal += "Shift+";
        if (modifiers.HasFlag(ModifierKeys.Alt)) proposal += "Alt+";
        if (modifiers.HasFlag(ModifierKeys.Windows)) proposal += "Win+";
        proposal += key;
        string other = ReferenceEquals(_editingShortcut, TxtToggleHotkey)
            ? TxtSaveHotkey.Text : TxtToggleHotkey.Text;
        bool valid = HotkeyManager.IsValidHotkey(proposal) &&
            !HotkeyManager.AreEquivalentHotkeys(proposal, other);
        _shortcutCandidate = valid ? proposal : "";
        SaveShortcutButton.IsEnabled = valid;
        ShortcutHint.Text = valid ? proposal + " — select Save Shortcut to keep it."
            : "Choose a different combination with Ctrl or Alt; reserved and duplicate shortcuts are unavailable.";
        _recordingShortcut = false;
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
        PreviewNotificationRequested?.Invoke(true, ChkClipSavedSound.IsChecked == true, NotificationTone(), _customSoundPath);

    private string NotificationTone() => CmbNotificationTone.SelectedIndex switch { 1 => "Quiet", 2 => "Custom", _ => "Chime" };

    private void ChooseSound_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "PCM WAV audio (*.wav)|*.wav", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            _ = NotificationSoundFile.Read(picker.FileName);
            _customSoundPath = picker.FileName;
            CustomSoundName.Text = Path.GetFileName(_customSoundPath);
            CmbNotificationTone.SelectedIndex = 2;
        }
        catch (Exception ex) { FlickDialog.Show(ex.Message, "Choose a Short WAV Sound"); }
    }

    private void ResetSound_Click(object sender, RoutedEventArgs e)
    {
        _customSoundPath = "";
        CustomSoundName.Text = "No custom sound selected";
        CmbNotificationTone.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        LastSaveWarning = "";
        TxtSaveStatus.Text = "Checking and saving all preferences…";
        if (_editingShortcut != null)
        {
            ShortcutHint.Text = "Save or cancel the shortcut change before saving Settings.";
            TxtSaveStatus.Text = "Finish recording your shortcut, or cancel that edit, then Save Settings.";
            // Do not navigate categories here: that path cancels the pending
            // shortcut recording and would silently lose the user's chord.
            return; // Never silently apply or discard an unfinished shortcut.
        }
        if (!int.TryParse(TxtDuration.Text, out int duration) ||
            duration < 15 || duration > 120)
        {
            SelectCategory("Recording");
            LocalClip.App.Services.FlickDialog.Show("Replay Duration Must Be Between 15 and 120 Seconds.", "Invalid Duration");
            return;
        }
        int cap = GetShareCap();
        if (!ShareExportTargets.IsValid(cap))
        {
            SelectCategory("Sharing");
            LocalClip.App.Services.FlickDialog.Show("Select a File Limit Between 1 and 250 MB.", "Invalid Export Limit");
            return;
        }
        string toggle = TxtToggleHotkey.Text.Trim();
        string save = TxtSaveHotkey.Text.Trim();
        if (!HotkeyManager.IsValidHotkey(toggle) || !HotkeyManager.IsValidHotkey(save) ||
            HotkeyManager.AreEquivalentHotkeys(toggle, save))
        {
            SelectCategory("Shortcuts");
            LocalClip.App.Services.FlickDialog.Show("Choose Two Different Valid Hotkeys.", "Invalid Shortcuts");
            return;
        }
        string originals, exports;
        try
        {
            if (!Path.IsPathFullyQualified(TxtOriginalsFolder.Text.Trim()) ||
                !Path.IsPathFullyQualified(TxtExportsFolder.Text.Trim()))
                throw new ArgumentException("Select fully qualified paths.");
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
            LocalClip.App.Services.FlickDialog.Show("Invalid Storage Paths: " + ex.Message, "Storage Settings");
            return;
        }

        string webhook = TxtWebhookUrl.Password.Trim();
        bool enableWebhook = ChkEnableWebhook.IsChecked == true;
        if (enableWebhook && (string.IsNullOrWhiteSpace(webhook) ||
            !DiscordWebhookClient.ValidateWebhookUrl(webhook, out _)))
        {
            SelectCategory("Sharing");
            LocalClip.App.Services.FlickDialog.Show("A valid Discord webhook is required to enable optional direct posting.",
                "Invalid Webhook");
            return;
        }
        if (_isCaptureActive() && AudioRoutingChanged() &&
            LocalClip.App.Services.FlickDialog.Show("Changing audio routing stops the current recording. Unsaved replay footage will be discarded. Continue?",
                "Change Recording Audio", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (!ReviewGameSettingsBeforeSave()) return;

        bool originalStartup = _settings.StartWithWindows;
        bool originalMinimized = _settings.StartMinimizedToTray;
        bool startupTouched = false;
        try
        {
            // Stage all edits in a temporary snapshot: cancelling or an invalid
            // field never partially rewrites the live VM's settings.
            var staged = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings)) ??
                throw new InvalidOperationException("Could not stage current settings.");
            staged.SchemaVersion = 4;
            staged.GameDetectionEnabled = ChkGameDetectionEnabled.IsChecked == true;
            staged.ApprovedGameProfiles = _stagedGameProfiles.ToList();
            staged.CaptureToggleHotkey = toggle;
            staged.SaveReplayHotkey = save;
            staged.OriginalsFolder = originals;
            staged.ExportsFolder = exports;
            staged.ReplayDurationSeconds = duration;
            StageAudio(staged);
            staged.EnableMicrophone = ChkMicrophone.IsChecked == true;
            staged.AllowSoftwareFallback = ChkSoftwareFallback.IsChecked == true;
            staged.StartWithWindows = ChkStartWithWindows.IsChecked == true;
            staged.StartMinimizedToTray = ChkStartMinimized.IsChecked == true;
            staged.IncludePreviewUpdates = ChkPreviewUpdates.IsChecked == true;
            StageDisplay(staged);
            staged.ShowClipSavedOverlay = ChkClipSavedOverlay.IsChecked == true;
            staged.PlayClipSavedSound = ChkClipSavedSound.IsChecked == true;
            staged.ClipSavedSoundTone = NotificationTone();
            staged.ClipSavedSoundPath = _customSoundPath;
            staged.DiscordTargetSizeMb = cap;
            RecordingProfiles.TryApply(Preset(), staged);
            staged.DiscordWebhookChannelName = TxtWebhookChannel.Text.Trim();
            staged.MaskedWebhookUrl = string.IsNullOrWhiteSpace(webhook)
                ? "" : DiscordWebhookClient.ProtectSecret(webhook);
            staged.EnableDiscordWebhook = enableWebhook && webhook.Length > 0;

            Directory.CreateDirectory(originals);
            Directory.CreateDirectory(exports);
            // Windows startup is a separate OS registration; failure must not
            // discard unrelated settings such as RAM/CPU or tray preferences.
            // Only touch HKCU Run when its desired command actually changed.
            bool startupChanged = staged.StartWithWindows != originalStartup ||
                (staged.StartWithWindows && staged.StartMinimizedToTray != originalMinimized);
            if (startupChanged)
            {
                try
                {
                    _applyWindowsStartup(staged.StartWithWindows, staged.StartMinimizedToTray);
                    startupTouched = true;
                }
                catch (Exception startupError)
                {
                    // Don't persist a false claim that Windows startup was changed.
                    staged.StartWithWindows = originalStartup;
                    LastSaveWarning = "Other preferences were saved, but Windows startup registration was not changed: " +
                        startupError.Message;
                }
            }
            // Save all fields together and independently re-read the complete
            // persisted JSON before updating the running application's model.
            _saveReviewedSettings(staged);

            // Only this user-initiated Save may replace a recovered settings
            // file. Incidental startup/source changes are deliberately deferred.
            _settings.MarkExplicitSettingsSaveCompleted();

            // Commit only GUI-managed properties after the atomic disk write.
            _settings.SchemaVersion = staged.SchemaVersion;
            _settings.GameDetectionEnabled = staged.GameDetectionEnabled;
            _settings.ApprovedGameProfiles = staged.ApprovedGameProfiles.ToList();
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
            _settings.AudioMode = staged.AudioMode;
            _settings.GameAudioSource = staged.GameAudioSource;
            _settings.VoiceAudioSource = staged.VoiceAudioSource;
            _settings.SystemAudioDeviceId = staged.SystemAudioDeviceId;
            _settings.MicrophoneDeviceId = staged.MicrophoneDeviceId;
            _settings.EnableMicrophone = staged.EnableMicrophone;
            _settings.AllowSoftwareFallback = staged.AllowSoftwareFallback;
            _settings.StartWithWindows = staged.StartWithWindows;
            _settings.StartMinimizedToTray = staged.StartMinimizedToTray;
            _settings.ShowResourceUsage = staged.ShowResourceUsage;
            _settings.IncludePreviewUpdates = staged.IncludePreviewUpdates;
            _settings.ShowClipSavedOverlay = staged.ShowClipSavedOverlay;
            _settings.PlayClipSavedSound = staged.PlayClipSavedSound;
            _settings.ClipSavedSoundTone = staged.ClipSavedSoundTone;
            _settings.ClipSavedSoundPath = staged.ClipSavedSoundPath;
            _settings.DiscordTargetSizeMb = staged.DiscordTargetSizeMb;
            _settings.DiscordWebhookChannelName = staged.DiscordWebhookChannelName;
            _settings.MaskedWebhookUrl = staged.MaskedWebhookUrl;
            _settings.EnableDiscordWebhook = staged.EnableDiscordWebhook;
            TxtSaveStatus.Text = LastSaveWarning.Length > 0
                ? "Preferences saved with a Windows startup warning."
                : "All preferences saved and verified.";
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            if (startupTouched)
            {
                try { _applyWindowsStartup(originalStartup, originalMinimized); }
                catch { /* Warn: the original registry entry may need manual review. */ }
            }
            TxtSaveStatus.Text = "Save failed. No settings were applied: " + ex.Message;
            LocalClip.App.Services.FlickDialog.Show("Settings Couldn't Be Saved: " + ex.Message, "Flick Settings");
        }
    }

    private void UninstallFlick_Click(object sender, RoutedEventArgs e)
    {
        if (!FlickUninstallLauncher.IsInstalled)
        {
            LocalClip.App.Services.FlickDialog.Show(Window.GetWindow(this),
                "This is a portable copy or Flick was not installed normally. Quit Flick through the tray and delete the extracted application folder. Your videos and settings live separately.",
                "Portable Flick", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (LocalClip.App.Services.FlickDialog.Show(Window.GetWindow(this),
                "Uninstall Flick now? Any active replay buffer will be stopped. The uninstaller will ask separately whether to remove Flick settings and caches. Original recordings and exported videos are kept.",
                "Uninstall Flick", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        UninstallRequested = true;
        CloseRequested?.Invoke(false);
    }

    private void Settings_KeyDown(object sender, KeyEventArgs e)
    {
        // Avoid stealing Escape from the shortcut capture fields; their own
        // handler uses it to restore the old chord without exiting Settings.
        if (e.Key == Key.Escape && _editingShortcut != null) { CancelShortcutEdit(); e.Handled = true; return; }
        if (e.Key != Key.Escape || e.Handled ||
            ReferenceEquals(Keyboard.FocusedElement, TxtToggleHotkey) ||
            ReferenceEquals(Keyboard.FocusedElement, TxtSaveHotkey))
            return;

        e.Handled = true;
        CloseRequested?.Invoke(false); // Cancel staged changes, stay in Flick.
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(false);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (LocalClip.App.Services.FlickDialog.Show("Restore the default settings? Existing clips and exported files will not be deleted.",
            "Restore Flick Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
            MessageBoxResult.Yes)
            Populate(AppSettings.CreateDefault()); // Still staged; Save applies it.
    }
}
