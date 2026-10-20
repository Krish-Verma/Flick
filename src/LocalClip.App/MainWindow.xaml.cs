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
using LocalClip.Core.Models;

namespace LocalClip.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly HotkeyManager _hotkeyManager;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private readonly DispatcherTimer _playerTimer;
    private bool _isDraggingSlider = false;
    private bool _isRealClosing = false;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.OnCaptureToggleHotkeyPressed += async () => await _vm.ToggleCaptureAsync();
        _hotkeyManager.OnSaveReplayHotkeyPressed += async () => await _vm.SaveReplayAsync();

        _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _playerTimer.Tick += PlayerTimer_Tick;

        Loaded += MainWindow_Loaded;
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
        BtnSaveReplay.Content = $"SAVE CLIP ({_vm.Settings.SaveReplayHotkey})";
    }

    private void SetupSystemTray()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Flick - Manual Gameplay Clipper",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Flick", null, (s, e) => ShowAndRestoreWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Start/Stop Replay", null, async (s, e) => await _vm.ToggleCaptureAsync());
        menu.Items.Add("Save Replay Clip", null, async (s, e) => await _vm.SaveReplayAsync());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (s, e) => RealQuit());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => ShowAndRestoreWindow();

        // Only report a confirmed saved file, from the UI dispatcher.
        _vm.CaptureHost.ReplaySaved += (id, path) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (File.Exists(path))
                    _notifyIcon?.ShowBalloonTip(2000, "Flick",
                        $"Saved replay clip: {Path.GetFileName(path)}", System.Windows.Forms.ToolTipIcon.Info);
            }));
    }

    private void UpdateStatusBadgeColor()
    {
        if (_vm.CaptureStatus == "Armed")
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
            BtnToggleCapture.Content = $"DISARM REPLAY ({_vm.Settings.CaptureToggleHotkey})";
            BtnToggleCapture.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
        }
        else if (_vm.CaptureStatus == "Buffering")
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(234, 179, 8)); // Yellow
            BtnToggleCapture.Content = $"DISARM REPLAY ({_vm.Settings.CaptureToggleHotkey})";
            BtnToggleCapture.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
        }
        else if (_vm.CaptureStatus == "Faulted")
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            BtnToggleCapture.Content = $"ARM REPLAY ({_vm.Settings.CaptureToggleHotkey})";
            BtnToggleCapture.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Green
        }
        else
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(63, 63, 70)); // Gray
            BtnToggleCapture.Content = $"ARM REPLAY ({_vm.Settings.CaptureToggleHotkey})";
            BtnToggleCapture.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Green
        }
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
            _notifyIcon?.ShowBalloonTip(2000, "Flick", "Running in system tray. Right click tray icon to quit.", System.Windows.Forms.ToolTipIcon.Info);
            return;
        }

        _playerTimer.Stop();
        _hotkeyManager.Dispose();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _vm.Dispose();
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

    private async void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.CaptureStatus != "Off")
        {
            if (MessageBox.Show("Recording settings require stopping the current replay buffer. Stop and open settings?",
                "Flick settings", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            await _vm.StopCaptureAsync();
        }
        var dlg = new SettingsWindow(_vm.Settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            RegisterHotkeys();
            UpdateStatusBadgeColor();
            _vm.StatusDetails = "Preferences saved. Replay capture remains disarmed until manually started.";
        }
    }

    // Gallery Handlers
    private void BtnOpenMediaFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.OpenExportFolder();
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