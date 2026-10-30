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
/// One organized settings surface. All changes are staged until
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
    private readonly Func<bool, WindowsStartupStatus> _readWindowsStartupStatus;
    public string LastSaveWarning { get; private set; } = "";
    private string _customSoundPath = "";
    private bool _loading;
    private TextBox? _editingShortcut;
    private string _shortcutBeforeEdit = "";
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
        Action<AppSettings>? saveReviewedSettings = null, Action<bool, bool>? applyWindowsStartup = null,
        Func<bool, WindowsStartupStatus>? readWindowsStartupStatus = null)
    {
        InitializeComponent();
        _settings = settings;
        _isCaptureActive = isCaptureActive ?? throw new ArgumentNullException(nameof(isCaptureActive));
        _loadAudioSources = loadAudioSources;
        _loadAudioEndpoints = loadAudioEndpoints ?? WindowsAudioEndpointEnumerator.Enumerate;
        _saveReviewedSettings = saveReviewedSettings ??
            (reviewed => reviewed.SaveReviewedToFile(AppSettings.GetSettingsFilePath()));
        _applyWindowsStartup = applyWindowsStartup ?? WindowsStartupRegistration.Apply;
        _readWindowsStartupStatus = readWindowsStartupStatus ?? (applyWindowsStartup == null
            ? WindowsStartupRegistration.ReadStatus
            : _ => new WindowsStartupStatus(WindowsStartupRegistrationState.Registered, "Registered for Windows startup."));
        Populate(settings);
        SelectCategory("Recording");
        TxtCurrentVersion.Text = "Version " + FlickUpdateService.CurrentVersion;
        ChkPreviewUpdates.IsChecked = settings.IncludePreviewUpdates;
        TxtUpdateMode.Text = FlickUninstallLauncher.IsInstalled
            ? "Updates keep your clips and preferences."
            : "Portable copy · download a new ZIP to update.";
        TxtUpdateMode.ToolTip = FlickUninstallLauncher.IsInstalled
            ? "Save pending settings before installing an update."
            : "Quit Flick before replacing the extracted application folder. Clips and preferences live separately.";
        Unloaded += (_, _) => LeavePage();
    }

    /// <summary>Leaving Settings cancels network work and removes this page's staged installer.</summary>
    public void LeavePage()
    {
        CancelShortcutEdit();
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
                    ? " Ready to download."
                    : " Open Release Notes for the ZIP.");
        }
        catch (OperationCanceledException)
        { if (ReferenceEquals(_updateCancellation, cancellation)) TxtUpdateStatus.Text = "Update check cancelled."; }
        catch (Exception)
        { if (ReferenceEquals(_updateCancellation, cancellation)) TxtUpdateStatus.Text = "Couldn't check for updates. Try again or open Release Notes."; }
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
            TxtUpdateStatus.Text = $"Flick {download.Release.Version} verified. Ready to install.";
            TxtUpdateStatus.ToolTip = "Save pending settings and stop replay before installing.";
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
            ChkKeepReplayReadyInBackground.IsChecked = settings.KeepReplayReadyInBackground;
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
        SaveShortcutLabel.Text = $"Save last {settings.ReplayDurationSeconds}s";
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
                SaveShortcutLabel.Text = $"Save last {seconds}s";
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
            TxtBufferWarning.Text = "Choose 15–120 seconds.";
            return;
        }
        var target = AppSettings.CreateDefault();
        if (!RecordingProfiles.TryApply(Preset(), target)) return;
        double estimate = RecordingProfiles.EstimatedReplayBytes(
            duration, target.VideoBitrateKbps, ChkMicrophone?.IsChecked == true) / 1_048_576d;
        TxtBufferWarning.Text = estimate > target.BufferMemoryLimitMb
            ? $"~{estimate:F0} MiB replay · may exceed the buffer limit"
            : $"~{estimate:F0} MiB replay buffer";
        TxtBufferWarning.ToolTip = $"Estimated compressed video/audio packets, not total app RAM. Limit: {target.BufferMemoryLimitMb} MiB. Clips may be shorter when the limit is reached.";
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
        _shortcutBeforeEdit = _editingShortcut.Text;
        // Capture keys rather than free-form text, paste or IME composition.
        // The prompt is display-only and is restored before any Settings Save.
        _editingShortcut.Text = "Press a shortcut…";
        _editingShortcut.SetResourceReference(Control.ForegroundProperty, "Flick.Brush.TextPrimary");
        _editingShortcut.SetResourceReference(Control.BorderBrushProperty, "Flick.Brush.Focus");
        System.Windows.Automation.AutomationProperties.SetHelpText(_editingShortcut,
            "Press Ctrl or Alt with a key. Escape cancels.");
        ShortcutEditor.Visibility = Visibility.Visible;
        ShortcutHint.Text = "Ctrl or Alt + a key. Esc cancels.";
        ShortcutEditingChanged?.Invoke(true);
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_editingShortcut), _editingShortcut);
        _editingShortcut.Focus();
    }

    private void FinishShortcutEdit(string? accepted = null)
    {
        TextBox? field = _editingShortcut;
        _editingShortcut = null;
        if (field != null)
        {
            field.Text = accepted ?? _shortcutBeforeEdit;
            field.SetResourceReference(Control.ForegroundProperty, "Flick.Brush.TextSecondary");
            field.ClearValue(Control.BorderBrushProperty);
            System.Windows.Automation.AutomationProperties.SetHelpText(field,
                "Choose Edit to change this shortcut.");
        }
        _shortcutBeforeEdit = "";
        if (ShortcutEditor != null) ShortcutEditor.Visibility = Visibility.Collapsed;
        if (ShortcutHint != null) ShortcutHint.Text = "";
        if (field != null) ShortcutEditingChanged?.Invoke(false);
    }

    private void CancelShortcutEdit() => FinishShortcutEdit();

    private void CancelShortcut_Click(object sender, RoutedEventArgs e) => CancelShortcutEdit();

    private void Shortcut_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _editingShortcut)) CancelShortcutEdit();
    }

    private void Shortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editingShortcut is null ||
            !ReferenceEquals(sender, _editingShortcut)) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;
        // Tab/Shift+Tab still navigate; Ctrl+Tab remains recordable whenever
        // the shared shortcut policy permits that exact modifier chord.
        if (key == Key.Tab && (modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            CancelShortcutEdit();
            return;
        }
        e.Handled = true;
        CaptureShortcut(key, modifiers);
    }

    private void CaptureShortcut(Key key, ModifierKeys modifiers)
    {
        if (_editingShortcut is null) return;
        if (key == Key.Escape && (modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            CancelShortcutEdit();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None or
            Key.System or Key.ImeProcessed or Key.DeadCharProcessed) return;
        string proposal = "";
        if (modifiers.HasFlag(ModifierKeys.Control)) proposal += "Ctrl+";
        if (modifiers.HasFlag(ModifierKeys.Shift)) proposal += "Shift+";
        if (modifiers.HasFlag(ModifierKeys.Alt)) proposal += "Alt+";
        if (modifiers.HasFlag(ModifierKeys.Windows)) proposal += "Win+";
        proposal += key;
        string other = ReferenceEquals(_editingShortcut, TxtToggleHotkey)
            ? TxtSaveHotkey.Text : TxtToggleHotkey.Text;
        if (!HotkeyManager.IsValidHotkey(proposal))
        {
            ShortcutHint.Text = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.None
                ? "Use Ctrl or Alt with a key."
                : "That shortcut is reserved. Try another key.";
            return;
        }
        if (HotkeyManager.AreEquivalentHotkeys(proposal, other))
        {
            ShortcutHint.Text = "Already used by the other shortcut.";
            return;
        }
        TextBox field = _editingShortcut;
        FinishShortcutEdit(proposal); // Only staged; the global Save persists it.
        (ReferenceEquals(field, TxtToggleHotkey) ? BtnEditToggleHotkey : BtnEditSaveHotkey).Focus();
    }

    private void BrowseOriginals_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a clips folder",
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
            Description = "Choose an exports folder",
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
        catch (Exception ex) { FlickDialog.Show(ex.Message, "Choose a short WAV sound"); }
    }

    private void ResetSound_Click(object sender, RoutedEventArgs e)
    {
        _customSoundPath = "";
        CustomSoundName.Text = "No custom sound selected";
        CmbNotificationTone.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CancelShortcutEdit();
        LastSaveWarning = "";
        TxtSaveStatus.Text = "Saving…";
        TxtSaveStatus.ToolTip = null;
        if (!int.TryParse(TxtDuration.Text, out int duration) ||
            duration < 15 || duration > 120)
        {
            ShowSaveValidation("Recording", TxtDuration, "Choose 15–120 seconds.");
            return;
        }
        int cap = GetShareCap();
        if (!ShareExportTargets.IsValid(cap))
        {
            ShowSaveValidation("Sharing", TxtShareCustomMb, "Choose a file limit from 1 to 250 MB.");
            return;
        }
        string toggle = TxtToggleHotkey.Text.Trim();
        string save = TxtSaveHotkey.Text.Trim();
        if (!HotkeyManager.IsValidHotkey(toggle) || !HotkeyManager.IsValidHotkey(save) ||
            HotkeyManager.AreEquivalentHotkeys(toggle, save))
        {
            ShowSaveValidation("Shortcuts", TxtToggleHotkey, "Choose two different valid shortcuts.");
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
            ShowSaveValidation("Storage", TxtOriginalsFolder, "Choose separate folders for clips and exports.");
            TxtSaveStatus.ToolTip = ex.Message;
            return;
        }

        string webhook = TxtWebhookUrl.Password.Trim();
        bool enableWebhook = ChkEnableWebhook.IsChecked == true;
        if (enableWebhook && (string.IsNullOrWhiteSpace(webhook) ||
            !DiscordWebhookClient.ValidateWebhookUrl(webhook, out _)))
        {
            ShowSaveValidation("Sharing", TxtWebhookUrl, "Enter a valid Discord webhook or turn off direct posting.");
            return;
        }
        if (_isCaptureActive() && AudioRoutingChanged() &&
            LocalClip.App.Services.FlickDialog.Show("Changing audio routing stops the current recording. Unsaved replay footage will be discarded. Continue?",
                "Change recording audio", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            TxtSaveStatus.Text = "Changes not saved.";
            return;
        }

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
            try { StageAudio(staged); }
            catch (ArgumentException audioError)
            {
                ShowSaveValidation("Audio", CmbAudioMode, audioError.Message);
                return;
            }
            staged.EnableMicrophone = ChkMicrophone.IsChecked == true;
            staged.AllowSoftwareFallback = ChkSoftwareFallback.IsChecked == true;
            staged.StartWithWindows = ChkStartWithWindows.IsChecked == true;
            staged.StartMinimizedToTray = ChkStartMinimized.IsChecked == true;
            staged.KeepReplayReadyInBackground = ChkKeepReplayReadyInBackground.IsChecked == true;
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
            // An enabled preference may outlive a missing/stale Run entry. A
            // deliberate Save repairs and verifies it even when JSON is unchanged.
            // Disabled, unrelated Saves never create or modify startup entries.
            if (staged.StartWithWindows || staged.StartWithWindows != originalStartup)
            {
                try
                {
                    _applyWindowsStartup(staged.StartWithWindows, staged.StartMinimizedToTray);
                    startupTouched = true;
                    if (staged.StartWithWindows)
                    {
                        var status = _readWindowsStartupStatus(staged.StartMinimizedToTray);
                        if (status.State == WindowsStartupRegistrationState.MissingOrStale)
                            throw new IOException("Windows startup registration could not be verified.");
                        if (status.State != WindowsStartupRegistrationState.Registered)
                            LastSaveWarning = status.Message;
                    }
                }
                catch (Exception startupError)
                {
                    // Don't persist a false claim that Windows startup was changed.
                    staged.StartWithWindows = originalStartup;
                    LastSaveWarning = "Windows startup couldn't be updated. Open Settings to retry.";
                    TxtSaveStatus.ToolTip = startupError.Message;
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
            _settings.KeepReplayReadyInBackground = staged.KeepReplayReadyInBackground;
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
                ? "Saved. Review Windows startup."
                : "Settings saved.";
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            if (startupTouched)
            {
                try { _applyWindowsStartup(originalStartup, originalMinimized); }
                catch { /* Warn: the original registry entry may need manual review. */ }
            }
            TxtSaveStatus.Text = "Couldn't save settings. Your changes are still here.";
            TxtSaveStatus.ToolTip = ex.Message;
            LocalClip.App.Services.FlickDialog.Show("Settings couldn't be saved: " + ex.Message, "Save Settings");
        }
    }

    private void ShowSaveValidation(string category, FrameworkElement field, string message)
    {
        SelectCategory(category);
        TxtSaveStatus.Text = message;
        TxtSaveStatus.ToolTip = message;
        field.Focus();
        field.BringIntoView();
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
        if (e.Key != Key.Escape || e.Handled ||
            (Keyboard.Modifiers & ~ModifierKeys.Shift) != ModifierKeys.None)
            return;

        if (_editingShortcut != null) { CancelShortcutEdit(); e.Handled = true; return; }

        e.Handled = true;
        CloseRequested?.Invoke(false); // Cancel staged changes, stay in Flick.
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelShortcutEdit();
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
