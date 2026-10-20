using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using LocalClip.App.Hotkeys;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.App.Services;
using System.Runtime.InteropServices;
using LocalClip.Core.Models;

namespace LocalClip.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly HotkeyManager _hotkeyManager;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayReplayItem;
    private System.Windows.Forms.ToolStripMenuItem? _traySaveItem;
    private System.Drawing.Icon? _trayBrandIcon;
    private bool _ownsTrayBrandIcon;
    private readonly FlickNotificationService _notificationService;
    private bool _trayHintShown;
    private readonly DispatcherTimer _playerTimer;
    private bool _isDraggingSlider = false;
    private bool _isRealClosing = false;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _notificationService = new FlickNotificationService(Dispatcher);
        _vm.ConfirmTargetReset = ConfirmSourceReset;
        _vm.ClipSavedConfirmed += OnClipSavedConfirmed;
        _vm.ReplaySaveFailed += OnReplaySaveFailed;

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.OnCaptureToggleHotkeyPressed += async () => await _vm.ToggleCaptureAsync();
        _hotkeyManager.OnSaveReplayHotkeyPressed += async () => await _vm.SaveReplayAsync();

        _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _playerTimer.Tick += PlayerTimer_Tick;

        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) => _ = FlickBackdropService.TryApplyMica(this);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeyManager.Initialize(hwnd);

        // Register default shortcuts
        RegisterHotkeys();

        // Setup Tray Icon
        SetupSystemTray();

        // Initialize targets
        await _vm.InitializeAsync();

        // Startup from Windows Run is always disarmed; optionally hide the window.
        if (_vm.Settings.StartMinimizedToTray ||
            Environment.GetCommandLineArgs().Any(a =>
                a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
            _ = Dispatcher.BeginInvoke(new Action(Hide));

        // Wire CaptureStatus badge color updates
        _vm.PropertyChanged += (s, ev) =>
        {
            if (ev.PropertyName == nameof(MainViewModel.CaptureStatus))
                UpdateStatusBadgeColor();
            else if (ev.PropertyName == nameof(MainViewModel.SelectedClip))
            {
                VideoPlayer.Stop();
                _playerTimer.Stop();
                BtnPlayPause.Content = "▶ Play";
                var clipPath = _vm.SelectedClip?.OriginalFilePath;
                VideoPlayer.Source = !string.IsNullOrEmpty(clipPath) && File.Exists(clipPath)
                    ? new Uri(clipPath) : null;
            }
        };
        UpdateStatusBadgeColor();
        UpdateSharePresetSelection();
        RadioAllClips.IsChecked = true;
    }

    private void RegisterHotkeys()
    {
        string? oldToggle = _hotkeyManager.RegisteredToggleHotkey;
        string? oldSave = _hotkeyManager.RegisteredSaveHotkey;
        if (!_hotkeyManager.TryConfigure(_vm.Settings.CaptureToggleHotkey,
                _vm.Settings.SaveReplayHotkey, out string error))
        {
            MessageBox.Show(error + " The previous shortcuts were restored if possible.",
                "Flick hotkey conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (oldToggle != null && oldSave != null)
            {
                _vm.Settings.CaptureToggleHotkey = oldToggle;
                _vm.Settings.SaveReplayHotkey = oldSave;
                _vm.Settings.Save();
            }
        }
        BtnSaveReplay.ToolTip = $"Save a replay ({_vm.Settings.SaveReplayHotkey})";
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private System.Drawing.Icon CreateTrayIcon()
    {
        try
        {
            var asset = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/Brand/FlickLogo.png"));
            if (asset == null) return System.Drawing.SystemIcons.Application;
            using var stream = asset.Stream;
            using var bitmap = new System.Drawing.Bitmap(stream);
            IntPtr native = bitmap.GetHicon();
            try
            {
                var copy = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(native).Clone();
                _ownsTrayBrandIcon = true;
                return copy;
            }
            finally { _ = DestroyIcon(native); }
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    private void SetupSystemTray()
    {
        _trayBrandIcon = CreateTrayIcon();
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayBrandIcon,
            Text = "Flick — Replay Off",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Flick", null, (sender, args) =>
            _ = Dispatcher.BeginInvoke(new Action(ShowAndRestoreWindow)));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _trayReplayItem = new System.Windows.Forms.ToolStripMenuItem("Start Replay");
        _trayReplayItem.Click += (sender, args) =>
            _ = Dispatcher.BeginInvoke(new Action(async () => await _vm.ToggleCaptureAsync()));
        menu.Items.Add(_trayReplayItem);

        _traySaveItem = new System.Windows.Forms.ToolStripMenuItem("Save Last 30 Seconds")
        {
            Enabled = false
        };
        _traySaveItem.Click += (sender, args) =>
            _ = Dispatcher.BeginInvoke(new Action(async () => await _vm.SaveReplayAsync()));
        menu.Items.Add(_traySaveItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        menu.Items.Add("Settings", null, (sender, args) =>
            _ = Dispatcher.BeginInvoke(new Action(() => OpenSettings())));
        menu.Items.Add("Quit Flick", null, (sender, args) =>
            _ = Dispatcher.BeginInvoke(new Action(RealQuit)));

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (sender, args) =>
            _ = Dispatcher.BeginInvoke(new Action(ShowAndRestoreWindow));
    }

    private void OnClipSavedConfirmed(ClipMetadata clip)
    {
        if (_isRealClosing) return;
        // Only the verified, indexed file produces a success indication.
        try
        {
            _notificationService.NotifySaved(
                clip.Title, clip.DurationSeconds,
                _vm.Settings.ShowClipSavedOverlay,
                _vm.Settings.PlayClipSavedSound);
        }
        catch (Exception)
        {
            // Nonessential overlays must never break capture or file persistence.
            if (_vm.Settings.ShowClipSavedOverlay)
                _notifyIcon?.ShowBalloonTip(1800, "Flick · Clip Saved",
                    System.IO.Path.GetFileName(clip.OriginalFilePath),
                    System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void OnReplaySaveFailed(string reason)
    {
        if (_isRealClosing) return;
        try
        {
            _notificationService.NotifySaveFailed(reason, _vm.Settings.ShowClipSavedOverlay);
        }
        catch
        {
            // Real failure remains visible in the application's status bar.
        }
    }

    private void UpdateStatusBadgeColor()
    {
        string status = _vm.CaptureStatus;
        bool ready = status == "Armed";
        bool buffering = status == "Buffering";
        bool active = ready || buffering;
        bool transitional = status == "Starting" || status == "Stopping";
        bool fault = status == "Faulted";

        TxtCaptureStatus.Text = status switch
        {
            "Armed" => "Replay Ready",
            "Buffering" => "Buffering Replay",
            "Starting" => "Starting Replay",
            "Stopping" => "Stopping Replay",
            "Faulted" => "Capture Error",
            "Paused" => "Replay Paused",
            _ => "Replay Off"
        };

        var neutral = (System.Windows.Media.Brush)FindResource("Flick.Brush.TextMuted");
        var green = (System.Windows.Media.Brush)FindResource("Flick.Brush.Success");
        var amber = (System.Windows.Media.Brush)FindResource("Flick.Brush.Warning");
        var danger = (System.Windows.Media.Brush)FindResource("Flick.Brush.Danger");
        CaptureStateDot.Fill = ready ? green : buffering || transitional ? amber :
            fault ? danger : neutral;
        StatusBadge.Background = new SolidColorBrush(Color.FromArgb(225, 47, 38, 60));

        BtnToggleCapture.Content = active || transitional ? "Stop Replay" : "Start Replay";
        BtnToggleCapture.Style = (Style)FindResource(
            active || transitional ? "Flick.Button.Secondary" : "Flick.Button.Primary");
        BtnToggleCapture.IsEnabled = !transitional;
        BtnToggleCapture.ToolTip = $"Toggle Replay ({_vm.Settings.CaptureToggleHotkey})";

        string saveLabel = $"Save Last {_vm.Settings.ReplayDurationSeconds} Seconds";
        BtnSaveReplay.Content = saveLabel;
        BtnSaveReplay.ToolTip = $"Save a replay ({_vm.Settings.SaveReplayHotkey})";
        BtnSaveReplay.IsEnabled = active;
        BtnSaveReplay.Style = (Style)FindResource(
            active ? "Flick.Button.Primary" : "Flick.Button.Secondary");

        if (_trayReplayItem != null)
        {
            _trayReplayItem.Text = active || transitional ? "Stop Replay" : "Start Replay";
            _trayReplayItem.Enabled = !transitional;
        }
        if (_traySaveItem != null)
        {
            _traySaveItem.Text = saveLabel;
            _traySaveItem.Enabled = active;
        }
        if (_notifyIcon != null)
            _notifyIcon.Text = $"Flick — {TxtCaptureStatus.Text}";
    }

    private void ShowAndRestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void RealQuit()
    {
        _isRealClosing = true;
        Close();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_isRealClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _notifyIcon?.ShowBalloonTip(1800, "Flick",
                    "Still in the tray. Right-click Flick to stop replay or quit.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            return;
        }

        _playerTimer.Stop();
        _vm.ConfirmTargetReset = null;
        FlickBackdropService.Reset(this);
        _vm.ClipSavedConfirmed -= OnClipSavedConfirmed;
        _vm.ReplaySaveFailed -= OnReplaySaveFailed;
        _notificationService.Dispose();
        _hotkeyManager.Dispose();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        if (_ownsTrayBrandIcon)
            _trayBrandIcon?.Dispose();
        _vm.Dispose();
    }

    private bool ConfirmSourceReset()
    {
        return MessageBox.Show(
            "Changing the capture source will stop replay and clear the current unsaved buffer. Continue?",
            "Change Capture Source", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;
    }

    private void LibraryFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm is null || !IsLoaded) return;
        if (ReferenceEquals(sender, RadioAllClips)) _vm.ClipSourceFilter = "All Clips";
        else if (ReferenceEquals(sender, RadioFlickClips)) _vm.ClipSourceFilter = "Flick";
        else if (ReferenceEquals(sender, RadioImportedClips)) _vm.ClipSourceFilter = "Imported";
    }

    private void ClipList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject node) return;
        while (node != null && node is not ListBoxItem)
        {
            if (node is System.Windows.Media.Visual ||
                node is System.Windows.Media.Media3D.Visual3D)
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            else if (node is FrameworkContentElement content)
                node = content.Parent;
            else break;
        }
        if (node is ListBoxItem item)
            item.IsSelected = true;
    }

    private void BtnImportFolder_Click(object sender, RoutedEventArgs e) =>
        OpenConnectedFolders(this);

    private void OpenConnectedFolders(Window owner)
    {
        var dialog = new ConnectedFoldersWindow(_vm) { Owner = owner };
        dialog.ShowDialog();
        _vm.ReloadClips();
        _vm.RefreshConnectedFolders();
    }

    private void BtnNavClips_Click(object sender, RoutedEventArgs e)
    {
        TxtClipSearch.Focus();
    }

    // Top Bar Handlers
    private async void BtnToggleCapture_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ToggleCaptureAsync();
    }

    private async void BtnSaveReplay_Click(object sender, RoutedEventArgs e)
    {
        await _vm.SaveReplayAsync();
    }

    private async void BtnRefreshTargets_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshTargetsAsync();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        bool isCapturing = _vm.CaptureStatus == "Armed" ||
                           _vm.CaptureStatus == "Buffering";
        bool oldMic = _vm.Settings.EnableMicrophone;
        var dialog = new SettingsWindow(_vm.Settings, isCapturing) { Owner = this };
        dialog.ManageFoldersRequested += (sender, args) => OpenConnectedFolders(dialog);
        dialog.PreviewNotificationRequested += (show, sound) =>
        {
            try { _notificationService.Preview(sound); } catch { }
        };
        if (dialog.ShowDialog() != true) return;

        RegisterHotkeys();
        UpdateStatusBadgeColor();
        // Mic opt-out must release the device instead of leaving it open.
        if (isCapturing && oldMic && !_vm.Settings.EnableMicrophone)
        {
            _ = StopCaptureForMicPrivacyAsync();
            return;
        }
        _vm.StatusDetails = isCapturing
            ? "Preferences saved. New capture profile takes effect on the next manual replay start."
            : "Preferences saved. Replay remains off until manually started.";
    }

    private async System.Threading.Tasks.Task StopCaptureForMicPrivacyAsync()
    {
        try
        {
            await _vm.StopCaptureAsync();
            _vm.StatusDetails = "Microphone disabled and capture stopped. Manually start replay when ready.";
        }
        catch (Exception ex)
        {
            _vm.StatusDetails = "Couldn't stop the capture worker to release the microphone: " +
                                ex.Message + ". Quit Flick if the input remains active.";
        }
    }

    // Gallery Handlers
    private void BtnOpenMediaFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.OpenOriginalFolder();
    }

    private async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RescanLibraryAsync();
    }

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedClip == null) return;
        string current = _vm.SelectedClip.Title;
        // Simple input prompt dialog
        var w = new Window
        {
            Title = "Rename Clip",
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
            Foreground = new SolidColorBrush(Color.FromRgb(250, 250, 250))
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        var txt = new TextBox { Text = current, Margin = new Thickness(0, 8, 0, 16), Padding = new Thickness(6, 4, 6, 4) };
        var btn = new Button { Content = "Save", Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)) };
        btn.Click += (s, ev) => { w.DialogResult = true; w.Close(); };
        sp.Children.Add(new TextBlock { Text = "Enter new clip title:" });
        sp.Children.Add(txt);
        sp.Children.Add(btn);
        w.Content = sp;
        if (w.ShowDialog() == true)
        {
            _vm.RenameSelectedClip(txt.Text);
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        _vm.DeleteSelectedClip();
    }

    // Video Player & Transport
    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            double dur = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            PlayerSlider.Maximum = dur;

            // Never replace ORIGINAL trim bounds with the (shorter) export length.
            if (_vm.SelectedClip != null &&
                string.Equals(VideoPlayer.Source?.LocalPath, _vm.SelectedClip.OriginalFilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                double originalDuration = _vm.SelectedClip.DurationSeconds;
                SliderTrimStart.Maximum = originalDuration;
                SliderTrimEnd.Maximum = originalDuration;
                if (_vm.SelectedClip.TrimEndSeconds <= 0.0)
                    _vm.SelectedClip.TrimEndSeconds = originalDuration;
            }
        }
        _playerTimer.Start();
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Stop();
        BtnPlayPause.Content = "▶ Play";
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedClip == null || string.IsNullOrEmpty(_vm.SelectedClip.OriginalFilePath)) return;

        if (VideoPlayer.Source == null)
            VideoPlayer.Source = new Uri(_vm.SelectedClip.OriginalFilePath);

        if (BtnPlayPause.Content.ToString() == "▶ Play")
        {
            VideoPlayer.Play();
            BtnPlayPause.Content = "⏸ Pause";
        }
        else
        {
            VideoPlayer.Pause();
            BtnPlayPause.Content = "▶ Play";
        }
    }

    private void PreviewExport_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsExportReady || !File.Exists(_vm.ReadyExportFilePath))
        {
            _vm.StatusDetails = "Export this clip first to preview the final Discord mix.";
            return;
        }
        VideoPlayer.Source = new Uri(_vm.ReadyExportFilePath);
        VideoPlayer.Play();
        BtnPlayPause.Content = "⏸ Pause";
        _vm.StatusDetails = "Previewing exported H.264/AAC MP4 with selected audio levels.";
    }

    private void PreviewOriginal_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm.SelectedClip?.OriginalFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        VideoPlayer.Source = new Uri(path);
        VideoPlayer.Play();
        BtnPlayPause.Content = "⏸ Pause";
        _vm.StatusDetails = "Previewing original recording. Mix sliders affect the exported copy.";
    }

    private void PlayerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider)
        {
            VideoPlayer.Position = TimeSpan.FromSeconds(PlayerSlider.Value);
        }
    }

    private void PlayerTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            PlayerSlider.Value = VideoPlayer.Position.TotalSeconds;
            TxtPlayerTime.Text = $"{VideoPlayer.Position:m\\:ss} / {VideoPlayer.NaturalDuration.TimeSpan:m\\:ss}";
        }
    }

    private void PlayerSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void PlayerSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        VideoPlayer.Position = TimeSpan.FromSeconds(PlayerSlider.Value);
        _isDraggingSlider = false;
    }

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _vm.StatusDetails = "Windows could not preview this MKV. Open it in your default video player.";
        BtnPlayPause.Content = "▶ Play";
    }

    private void OpenInPlayer_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm.SelectedClip?.OriginalFilePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // Trim Handlers
    private void TrimSliders_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm.SelectedClip != null)
        {
            if (SliderTrimEnd.Value <= SliderTrimStart.Value && SliderTrimStart.Maximum > 0)
            {
                SliderTrimEnd.Value = Math.Min(SliderTrimStart.Value + 1.0, SliderTrimStart.Maximum);
            }
            _vm.SaveClipEdits();
        }
    }

    private void AudioMuteChanged(object sender, RoutedEventArgs e)
    {
        _vm.SaveClipEdits();
    }

    private void AudioGainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _vm.SaveClipEdits();
    }

    // Preset Clicks
    private void Preset20_Click(object sender, RoutedEventArgs e) { _vm.TargetSizeMb = 20; UpdateSharePresetSelection(); }
    private void Preset50_Click(object sender, RoutedEventArgs e) { _vm.TargetSizeMb = 50; UpdateSharePresetSelection(); }
    private void Preset100_Click(object sender, RoutedEventArgs e) { _vm.TargetSizeMb = 100; UpdateSharePresetSelection(); }

    private void PresetCustom_Click(object sender, RoutedEventArgs e) => TryApplyCustomSize();
    private void TxtCustomMb_LostFocus(object sender, RoutedEventArgs e)
    {
        if (RadioCustom.IsChecked == true) TryApplyCustomSize();
    }

    private void TryApplyCustomSize()
    {
        if (!int.TryParse(TxtCustomMb.Text, out int mb) || mb < 1 || mb > 250)
        {
            MessageBox.Show("Choose an export target between 1 and 250 MB.", "Invalid export size");
            UpdateSharePresetSelection();
            return;
        }
        _vm.TargetSizeMb = mb;
        UpdateSharePresetSelection();
    }

    private void UpdateSharePresetSelection()
    {
        int mb = _vm.TargetSizeMb;
        Radio20.IsChecked = mb == 20;
        Radio50.IsChecked = mb == 50;
        Radio100.IsChecked = mb == 100;
        RadioCustom.IsChecked = mb != 20 && mb != 50 && mb != 100;
        if (RadioCustom.IsChecked == true)
            TxtCustomMb.Text = mb.ToString();
    }

    // Export & Share
    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ExportForDiscordAsync();
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (File.Exists(_vm.ReadyExportFilePath))
        {
            // Initiate standard Windows OLE Drag & Drop of the actual file
            var data = new DataObject(DataFormats.FileDrop, new[] { _vm.ReadyExportFilePath });
            DragDrop.DoDragDrop(DragHandle, data, DragDropEffects.Copy);
        }
    }

    private void BtnCopyFile_Click(object sender, RoutedEventArgs e)
    {
        _vm.CopyExportedFileToClipboard();
    }

    private void BtnOpenExportFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.OpenExportFolder();
    }

    private async void BtnSendWebhook_Click(object sender, RoutedEventArgs e)
    {
        await _vm.SendToDiscordWebhookAsync();
    }
}