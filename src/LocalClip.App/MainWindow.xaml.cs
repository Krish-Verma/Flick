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
using System.Security.Cryptography;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Media;
using LocalClip.Core.Updates;
using LocalClip.App.Views.Components;
using System.Threading;

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
    private SettingsWindow? _settingsPage;
    private ConnectedFoldersWindow? _foldersPage;
    private bool _foldersReturnToSettings;
    private bool _settingsMicrophoneWasEnabled;
    private string _settingsAudioSnapshot = "";
    private bool _isDraggingSlider = false;
    private bool _isRealClosing = false;
    private System.Windows.Point? _shareDragStart;
    private bool _updatingExportCap;
    private enum PlayerView { Original, Exported, LocalCopy, AudioMix }
    private PlayerView _playerView = PlayerView.Original;
    private CancellationTokenSource? _previewCancellation;
    private MediaProber? _localPreviewProber;
    private bool _previewWasReencoded;
    private bool _playerIsPlaying;
    private WorkspaceLayoutMode _layoutMode = WorkspaceLayoutMode.Split;
    private bool _layoutInitialized;
    private bool _compactShowingEditor;
    private bool _initialLayoutApplied;
    // Only the currently selected small poster is unloaded while hidden; the
    // clip library and editing model are preserved for instant tray restoration.
    internal FrameworkElement InteractionSurface => MainInteractionSurface;
    internal Border ModalLayer => DialogLayer;
    internal ContentControl DialogHost => DialogContentHost;
    private bool _posterCacheDetached;
    private Button? _activeEditorTool;
    private bool _updateInstallInProgress;
    private bool _quitInProgress;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _notificationService = new FlickNotificationService(Dispatcher);
        _vm.ConfirmTargetReset = ConfirmSourceReset;
        _vm.DiagnosticReservationAllowed = () => !_updateInstallInProgress && !_quitInProgress && !_isRealClosing;
        _vm.ClipSavedConfirmed += OnClipSavedConfirmed;
        _vm.ReplaySaveFailed += OnReplaySaveFailed;

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.OnCaptureToggleHotkeyPressed += async () =>
        { if (!_updateInstallInProgress && !_quitInProgress) await _vm.ToggleCaptureAsync(); };
        _hotkeyManager.OnSaveReplayHotkeyPressed += async () =>
        { if (!_updateInstallInProgress && !_quitInProgress) await _vm.SaveReplayAsync(); };

        _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _playerTimer.Tick += PlayerTimer_Tick;

        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                SuspendPlayback(clearSource: true);
                ReleasePosterWhileHidden();
            }
            else if (WorkspacePageLayer.Visibility != Visibility.Visible)
                RestorePosterAfterShow();
        };
        SourceInitialized += (_, _) =>
        {
            // Native Windows caption stays dark without custom chrome or
            // sacrificing minimize/maximize/Win+Arrow/Snap/resize handling.
            _ = FlickBackdropService.TryApplyDarkTitleBar(this);
            _ = FlickBackdropService.TryApplyMica(this);
        };
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
        if (!string.IsNullOrWhiteSpace(_vm.Settings.LoadWarning))
            _vm.StatusDetails = _vm.Settings.LoadWarning;

        // Startup from Windows Run is always disarmed; optionally hide the window.
        if (_vm.Settings.StartMinimizedToTray ||
            Environment.GetCommandLineArgs().Any(a =>
                a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                SuspendPlayback(clearSource: true);
                ReleasePosterWhileHidden();
                Hide();
            }));

        // Wire CaptureStatus badge color updates
        _vm.PropertyChanged += (s, ev) =>
        {
            if (ev.PropertyName is nameof(MainViewModel.CaptureStatus) or nameof(MainViewModel.HealthStatusText)
                or nameof(MainViewModel.IsRecordingDiagnosticRunning))
            {
                UpdateStatusBadgeColor();
                if (_vm.CaptureStatus == "Starting" || _vm.IsRecordingDiagnosticRunning)
                {
                    // A real gameplay session always outranks a preview decoder.
                    _previewCancellation?.Cancel();
                    SuspendPlayback(clearSource: true);
                }
            }
            else if (ev.PropertyName == nameof(MainViewModel.AudioMixRevision))
            {
                CancelPendingPreview();
                if (_playerView is PlayerView.Exported or PlayerView.LocalCopy or PlayerView.AudioMix) ShowOriginalPreview(startPlaying: false);
            }
            else if (ev.PropertyName == nameof(MainViewModel.SelectedClip))
            {
                ResetPlayerForSelection();
                if (_vm.SelectedClip != null && _layoutMode == WorkspaceLayoutMode.Compact)
                    ShowCompactEditor();
            }
            else if (ev.PropertyName == nameof(MainViewModel.IsExportReady) &&
                     !_vm.IsExportReady && _playerView == PlayerView.Exported)
            {
                // The export became stale after edits, so never preview it as ready.
                ShowOriginalPreview(startPlaying: false);
            }
        };
        UpdateStatusBadgeColor();
        UpdateSharePresetSelection();
        RadioAllClips.IsChecked = true;
        ResetPlayerForSelection();
        _layoutInitialized = true;
        RefreshWorkspaceForSize();
    }

    private void RegisterHotkeys()
    {
        string? oldToggle = _hotkeyManager.RegisteredToggleHotkey;
        string? oldSave = _hotkeyManager.RegisteredSaveHotkey;
        if (!_hotkeyManager.TryConfigure(_vm.Settings.CaptureToggleHotkey,
                _vm.Settings.SaveReplayHotkey, out string error))
        {
            LocalClip.App.Services.FlickDialog.Show(error + " The previous shortcuts were restored if possible.",
                "Flick hotkey conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (oldToggle != null && oldSave != null)
            {
                _vm.Settings.CaptureToggleHotkey = oldToggle;
                _vm.Settings.SaveReplayHotkey = oldSave;
                _vm.Settings.Save();
            }
        }
        _vm.ReportHotkeyHealth("Toggle replay", _vm.Settings.CaptureToggleHotkey,
            _hotkeyManager.RegisteredToggleHotkey, error);
        _vm.ReportHotkeyHealth("Save replay", _vm.Settings.SaveReplayHotkey,
            _hotkeyManager.RegisteredSaveHotkey, error);
        BtnSaveReplay.ToolTip = $"Save a replay ({_vm.Settings.SaveReplayHotkey})";
    }

    private void BtnRecordingHealth_Click(object sender, RoutedEventArgs e)
    {
        RecordingHealthPanel.Visibility = Visibility.Visible;
        BtnCloseRecordingHealth.Focus();
        _ = _vm.RefreshDiagnosticsStatusAsync();
    }

    private async void BtnExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanExportDiagnostics) return;
        var dialog = new Microsoft.Win32.SaveFileDialog {
            Title = "Export Flick Diagnostics", Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true,
            FileName = "Flick-diagnostics-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".zip"
        };
        if (dialog.ShowDialog(this) == true)
            await _vm.ExportDiagnosticsAsync(dialog.FileName);
    }

    private async void BtnRunRecordingDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRecordingDiagnosticRunning) { _vm.CancelRecordingDiagnostic(); return; }
        var plan = _vm.CreateRecordingDiagnosticPlan(ChkDiagnosticAudio.IsChecked == true);
        if (plan == null) return;
        if (FlickDialog.Show(plan.ConsentSummary, "Run Recording Test?", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _vm.RunRecordingDiagnosticAsync(plan, plan.PlanId);
    }

    private void BtnCloseRecordingHealth_Click(object sender, RoutedEventArgs e)
    {
        RecordingHealthPanel.Visibility = Visibility.Collapsed;
        BtnRecordingHealth.Focus();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private System.Drawing.Icon CreateTrayIcon()
    {
        try { return CreateTrayIconCore(); }
        catch { return System.Drawing.SystemIcons.Application; }
    }

    private System.Drawing.Icon CreateTrayIconCore()
    {
        var asset = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Flick;component/Assets/Brand/FlickLogo.png"));
        if (asset == null) throw new IOException("The embedded Flick brand artwork was not found.");
        using var stream = asset.Stream;
        using var source = new System.Drawing.Bitmap(stream);
        // Match the WPF brand source and packaged ICO: keep a fixed dark
        // circle, and move only its original artwork by 1/38 of the canvas.
        const int size = 128;
        using var bitmap = new System.Drawing.Bitmap(size, size,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        using (var clip = new System.Drawing.Drawing2D.GraphicsPath())
        using (var canvas = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 6, 3, 13)))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            var viewport = new System.Drawing.RectangleF(0, 0, size, size);
            graphics.FillEllipse(canvas, viewport);
            clip.AddEllipse(viewport);
            graphics.SetClip(clip);
            float offset = size / 38f;
            graphics.DrawImage(source, new System.Drawing.RectangleF(offset, offset, size, size));
        }
        IntPtr native = bitmap.GetHicon();
        try
        {
            var copy = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(native).Clone();
            _ownsTrayBrandIcon = true;
            return copy;
        }
        finally { _ = DestroyIcon(native); }
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
            _ = Dispatcher.BeginInvoke(new Action(async () => { if (!_updateInstallInProgress && !_quitInProgress) await _vm.ToggleCaptureAsync(); }));
        menu.Items.Add(_trayReplayItem);

        _traySaveItem = new System.Windows.Forms.ToolStripMenuItem("Save Last 30 Seconds")
        {
            Enabled = false
        };
        _traySaveItem.Click += (sender, args) =>
            _ = Dispatcher.BeginInvoke(new Action(async () => { if (!_updateInstallInProgress && !_quitInProgress) await _vm.SaveReplayAsync(); }));
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
                clip.DurationSeconds,
                _vm.Settings.ShowClipSavedOverlay,
                _vm.Settings.PlayClipSavedSound,
                _vm.Settings.ClipSavedSoundTone, _vm.Settings.ClipSavedSoundPath);
        }
        catch (Exception)
        {
            // Nonessential overlays must never break capture or file persistence.
            if (_vm.Settings.ShowClipSavedOverlay)
                _notifyIcon?.ShowBalloonTip(1800, "Clip Saved",
                    NotificationWaveRules.DurationText(clip.DurationSeconds),
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
        bool ready = status == "Armed" && _vm.IsArmed;
        bool buffering = status == "Buffering";
        bool active = status is "Armed" or "Buffering";
        bool transitional = status == "Starting" || status == "Stopping";
        bool fault = status == "Faulted";
        bool testing = _vm.IsRecordingDiagnosticRunning;

        bool compact = _layoutMode == WorkspaceLayoutMode.Compact;
        TxtCaptureStatus.Text = testing ? "Testing Recording" : _vm.RecordingHealth.SessionId != Guid.Empty || _vm.RecordingHealth.LastSaveIssue != null
            ? _vm.HealthStatusText : status switch
        {
            "Armed" => compact ? "Ready" : "Replay Ready",
            "Buffering" => compact ? "Buffering" : "Buffering Replay",
            "Starting" => compact ? "Starting" : "Starting Replay",
            "Stopping" => compact ? "Stopping" : "Stopping Replay",
            "Faulted" => compact ? "Error" : "Capture Error",
            "Paused" => compact ? "Paused" : "Replay Paused",
            _ => compact ? "Off" : "Replay Off"
        };

        var neutral = (System.Windows.Media.Brush)FindResource("Flick.Brush.TextMuted");
        var green = (System.Windows.Media.Brush)FindResource("Flick.Brush.Success");
        var amber = (System.Windows.Media.Brush)FindResource("Flick.Brush.Warning");
        var danger = (System.Windows.Media.Brush)FindResource("Flick.Brush.Danger");
        CaptureStateDot.Fill = testing ? amber : ready ? green : buffering || transitional ? amber :
            fault ? danger : neutral;
        // Badge surface is a DynamicResource so a Windows high-contrast
        // theme can override it without stale fixed-color rendering.

        BtnToggleCapture.Content = active || transitional ? "Stop Replay" : "Start Replay";
        BtnToggleCapture.Style = (Style)FindResource(
            active || transitional ? "Flick.Button.Secondary" : "Flick.Button.Primary");
        BtnToggleCapture.IsEnabled = !transitional && _vm.CanControlReplay;
        BtnToggleCapture.ToolTip = $"Toggle Replay ({_vm.Settings.CaptureToggleHotkey})";

        string saveLabel = WorkspaceLayoutRules.SaveActionLabel(
            _vm.Settings.ReplayDurationSeconds, compact: false);
        BtnSaveReplay.Content = WorkspaceLayoutRules.SaveActionLabel(
            _vm.Settings.ReplayDurationSeconds, _layoutMode == WorkspaceLayoutMode.Compact);
        BtnSaveReplay.ToolTip = $"Save a replay ({_vm.Settings.SaveReplayHotkey})";
        // A buffering encoder is not yet guaranteed to hold a decodable replay.
        BtnSaveReplay.IsEnabled = ready && !testing;
        BtnSaveReplay.Style = (Style)FindResource(
            active ? "Flick.Button.Primary" : "Flick.Button.Secondary");

        if (_trayReplayItem != null)
        {
            _trayReplayItem.Text = active || transitional ? "Stop Replay" : "Start Replay";
            _trayReplayItem.Enabled = !transitional && _vm.CanControlReplay;
        }
        if (_traySaveItem != null)
        {
            _traySaveItem.Text = saveLabel;
            _traySaveItem.Enabled = ready && !testing;
        }
        if (_notifyIcon != null)
            _notifyIcon.Text = $"Flick — {TxtCaptureStatus.Text}";
    }

    private void ShowAndRestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        RestorePosterAfterShow();
        Activate();
        if (WorkspacePageLayer.Visibility != Visibility.Visible &&
            VideoPlayer.Source is null)
            ResetPlayerForSelection();
    }

    private async void RealQuit()
    {
        if (_quitInProgress || _isRealClosing) return;
        _quitInProgress = true;
        bool wasEnabled = IsEnabled;
        IsEnabled = false;
        SuspendPlayback(clearSource: true);
        _vm.StatusDetails = "Finishing replay saves before quitting...";
        try
        {
            if (!await _vm.PrepareToQuitAsync())
            {
                _vm.StatusDetails = "A replay save is still being finalized. Flick stayed open to protect it. Try quitting again after it finishes.";
                return;
            }
            _isRealClosing = true;
            Close();
        }
        catch (Exception error)
        {
            _vm.StatusDetails = "Flick could not finish closing safely: " + error.Message;
        }
        finally
        {
            if (!_isRealClosing)
            {
                IsEnabled = wasEnabled;
                _quitInProgress = false;
            }
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_isRealClosing)
        {
            e.Cancel = true;
            SuspendPlayback(clearSource: true);
            ReleasePosterWhileHidden();
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

        if (_foldersPage != null) _foldersPage.LeavePage();
        _foldersPage = null;
        _settingsPage?.LeavePage();
        _settingsPage = null;
        WorkspacePageHost.Content = null;
        _playerTimer.Stop();
        CancelPendingPreview();
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
        return LocalClip.App.Services.FlickDialog.Show(
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
        OpenConnectedFolders();

    private void ShowWorkspacePage(UserControl page)
    {
        // The editor may be hidden, but the independent capture host continues.
        // Abandon a pending FFmpeg local preview before leaving the editor.
        CancelPendingPreview();
        SuspendPlayback(clearSource: true);
        ReleasePosterWhileHidden();
        WorkspaceContentGrid.Visibility = Visibility.Collapsed;
        WorkspacePageHost.Content = page;
        WorkspacePageLayer.Visibility = Visibility.Visible;
        UpdateWorkspaceNavigation();
    }

    private void UpdateWorkspaceNavigation()
    {
        bool isClips = WorkspacePageLayer.Visibility != Visibility.Visible;
        BtnNavClips.Background = isClips
            ? (System.Windows.Media.Brush)FindResource("Flick.Brush.Raised")
            : System.Windows.Media.Brushes.Transparent;
        BtnNavSettings.Background = _settingsPage != null && _foldersPage == null
            ? (System.Windows.Media.Brush)FindResource("Flick.Brush.Raised")
            : System.Windows.Media.Brushes.Transparent;
    }

    private void ReturnToClipWorkspace()
    {
        _hotkeyManager.IsShortcutEditing = false;
        if (_foldersPage != null)
        {
            _foldersPage.CloseRequested -= CloseConnectedFolders;
            _foldersPage.LeavePage(); // Cancel only the current import scan.
            _foldersPage = null;
            _vm.ReloadClips();
            _vm.RefreshConnectedFolders();
        }
        if (_settingsPage != null)
        {
            _settingsPage.CloseRequested -= CloseSettingsPage;
            _settingsPage.InstallUpdateRequested -= InstallUpdateAsync;
            _settingsPage.LeavePage();
            _settingsPage = null; // Unsaved staged settings are discarded.
        }
        WorkspacePageHost.Content = null; // Do not retain invisible page UI.
        WorkspacePageLayer.Visibility = Visibility.Collapsed;
        WorkspaceContentGrid.Visibility = Visibility.Visible;
        RestorePosterAfterShow();
        UpdateWorkspaceNavigation();
        RefreshWorkspaceForSize();
    }

    private void OpenConnectedFolders()
    {
        if (FlickDialog.IsOpen) return;
        if (_foldersPage != null)
        {
            ShowWorkspacePage(_foldersPage);
            return;
        }
        _foldersReturnToSettings = _settingsPage != null;
        var folders = new ConnectedFoldersWindow(_vm);
        folders.CloseRequested += CloseConnectedFolders;
        _foldersPage = folders;
        ShowWorkspacePage(folders);
    }

    private void CloseConnectedFolders()
    {
        if (_foldersPage == null) return;
        _foldersPage.CloseRequested -= CloseConnectedFolders;
        _foldersPage.LeavePage();
        _foldersPage = null;
        _vm.ReloadClips();
        _vm.RefreshConnectedFolders();
        if (_foldersReturnToSettings && _settingsPage != null)
        {
            ShowWorkspacePage(_settingsPage); // Keep unsaved Settings edits.
            return;
        }
        ReturnToClipWorkspace();
    }

    private void BtnNavClips_Click(object sender, RoutedEventArgs e)
    {
        ReturnToClipWorkspace();
        if (_layoutMode == WorkspaceLayoutMode.Compact)
            ShowCompactLibrary();
        TxtClipSearch.Focus();
    }

    private void BtnBackToLibrary_Click(object sender, RoutedEventArgs e) =>
        BtnNavClips_Click(sender, e);

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshWorkspaceForSize();

    private void RefreshWorkspaceForSize()
    {
        if (!_layoutInitialized || ActualWidth <= 0) return;
        bool firstDecision = !_initialLayoutApplied;
        _initialLayoutApplied = true;
        var next = WorkspaceLayoutRules.Classify(ActualWidth);
        if (next == _layoutMode) return; // Avoid per-pixel layout churn while resizing.
        _layoutMode = next;
        if (next == WorkspaceLayoutMode.Compact)
            _compactShowingEditor = !firstDecision && _vm.SelectedClip != null;
        ApplyWorkspaceLayout();
        UpdateStatusBadgeColor();
    }

    private void ShowCompactEditor()
    {
        if (_layoutMode != WorkspaceLayoutMode.Compact) return;
        _compactShowingEditor = true;
        ApplyWorkspaceLayout();
    }

    private void ShowCompactLibrary()
    {
        if (_layoutMode != WorkspaceLayoutMode.Compact) return;
        _compactShowingEditor = false;
        ApplyWorkspaceLayout();
    }

    private void ApplyWorkspaceLayout()
    {
        bool compact = _layoutMode == WorkspaceLayoutMode.Compact;
        CaptureSourceColumn.Width = new GridLength(compact
            ? WorkspaceLayoutRules.CompactSourceWidthDips
            : WorkspaceLayoutRules.SplitSourceWidthDips);
        CmbTargets.Width = compact ? 163 : 235;
        TxtBufferedDuration.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TxtResourceStats.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BtnBackToLibrary.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ClipLibraryColumn.MinWidth = compact ? 0 : 255;
        ClipLibraryColumn.MaxWidth = compact ? double.PositiveInfinity : 430;
        LibrarySplitterColumn.Width = new GridLength(compact ? 0 : 4);
        LibrarySplitter.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (!compact)
        {
            ClipLibraryColumn.Width = new GridLength(WorkspaceLayoutRules.SplitLibraryWidthDips);
            ClipEditorColumn.Width = new GridLength(1, GridUnitType.Star);
            ClipLibraryPanel.Visibility = Visibility.Visible;
            ClipEditorPanel.Visibility = Visibility.Visible;
        }
        else if (_compactShowingEditor)
        {
            ClipLibraryColumn.Width = new GridLength(0);
            ClipEditorColumn.Width = new GridLength(1, GridUnitType.Star);
            ClipLibraryPanel.Visibility = Visibility.Collapsed;
            ClipEditorPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ClipLibraryColumn.Width = new GridLength(1, GridUnitType.Star);
            ClipEditorColumn.Width = new GridLength(0);
            ClipLibraryPanel.Visibility = Visibility.Visible;
            ClipEditorPanel.Visibility = Visibility.Collapsed;
        }
    }

    // Top Bar Handlers
    private async void BtnToggleCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInstallInProgress || _quitInProgress) return;
        await _vm.ToggleCaptureAsync();
    }

    private async void BtnSaveReplay_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInstallInProgress || _quitInProgress) return;
        await _vm.SaveReplayAsync();
    }

    private async void BtnRefreshTargets_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshTargetsAsync();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (FlickDialog.IsOpen || _updateInstallInProgress || _quitInProgress) return;
        if (_foldersPage != null) CloseConnectedFolders();
        if (_settingsPage != null)
        {
            ShowWorkspacePage(_settingsPage);
            return;
        }
        _settingsMicrophoneWasEnabled = _vm.Settings.EnableMicrophone;
        _settingsAudioSnapshot = AudioSettingsSnapshot();
        var settings = new SettingsWindow(_vm.Settings,
            () => _vm.IsCaptureActive, _vm.GetAudioSourcesAsync);
        settings.CloseRequested += CloseSettingsPage;
        settings.InstallUpdateRequested += InstallUpdateAsync;
        settings.ShortcutEditingChanged += editing => _hotkeyManager.IsShortcutEditing = editing;
        settings.ManageFoldersRequested += (_, _) => OpenConnectedFolders();
        settings.PreviewNotificationRequested += (show, sound, tone, path) =>
        {
            try { _notificationService.Preview(sound, tone, path); } catch { }
        };
        _settingsPage = settings;
        ShowWorkspacePage(settings);
    }

    private bool UpdateRequiresFinishingWork() =>
        _vm.IsCaptureActive || _vm.IsReplaySavePending || _vm.IsImporting || _vm.IsExporting ||
        _vm.IsBackgroundWorkActive;

    private async System.Threading.Tasks.Task<string?> InstallUpdateAsync(DownloadedUpdate download, CancellationToken cancellationToken)
    {
        var originatingSettings = _settingsPage;
        if (cancellationToken.IsCancellationRequested || originatingSettings == null ||
            !ReferenceEquals(WorkspacePageHost.Content, originatingSettings))
            return "Open Software Updates again to install the update.";
        if (_updateInstallInProgress) return "An update installation is already being prepared.";
        if (_quitInProgress) return "Flick is already finishing its work before closing.";
        if (!FlickUninstallLauncher.IsInstalled)
            return "This copy is portable. Open Release Notes for a new ZIP, then quit Flick and replace its extracted application folder.";
        if (UpdateRequiresFinishingWork())
            return "Save any replay you want to keep, stop replay yourself, and wait for clip saving, imports, library scans, exports and uploads to finish before installing. Flick will not stop or discard your work for an update.";

        _updateInstallInProgress = true;
        bool wasEnabled = MainInteractionSurface.IsEnabled;
        bool wasEditingShortcut = _hotkeyManager.IsShortcutEditing;
        MainInteractionSurface.IsEnabled = false;
        _hotkeyManager.IsShortcutEditing = true;
        try
        {
            // Native trust verification may contact Windows' revocation service;
            // keep it off the dispatcher and do not let hotkeys start capture meanwhile.
            using var prepared = await FlickUpdateService.PrepareInstallationAsync(download, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_isRealClosing || Dispatcher.HasShutdownStarted) return null;
            if (!ReferenceEquals(_settingsPage, originatingSettings) ||
                !ReferenceEquals(WorkspacePageHost.Content, originatingSettings))
                return "Open Software Updates again to install the update.";
            string warning = prepared.IsUnsignedPreview
                ? $"Install Flick {download.Release.Version}? This preview installer is unsigned. Its download checksum matches this GitHub release, but Windows has not verified its publisher. Smart App Control may block it. Continue only if you trust this release; Flick will not disable or bypass Windows protections. Flick will close and restart after installation. Pending Settings edits are not saved."
                : $"Install Flick {download.Release.Version} and restart? Windows verified the installer's signature. Flick will close while the update is installed. Saved clips and preferences are kept; pending Settings edits are not saved.";
            if (FlickDialog.Show(warning, prepared.IsUnsignedPreview ? "Install Unsigned Preview" : "Install Flick Update",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return "Installation cancelled. The downloaded update is ready when you are.";

            // The consent UI pumps dispatcher messages. Recheck live work, the
            // page and installation immediately before launching; never stop capture.
            cancellationToken.ThrowIfCancellationRequested();
            if (_isRealClosing || Dispatcher.HasShutdownStarted) return null;
            if (UpdateRequiresFinishingWork())
                return "New work started while preparing the update. Stop replay and wait for clip saving, imports, library scans, exports and uploads to finish, then try again.";
            if (!ReferenceEquals(_settingsPage, originatingSettings) ||
                !ReferenceEquals(WorkspacePageHost.Content, originatingSettings))
                return "Open Software Updates again to install the update.";
            cancellationToken.ThrowIfCancellationRequested();
            FlickUpdateService.LaunchInstaller(prepared);
            originatingSettings.MarkUpdateInstallerStarted();
            RealQuit(); // Normal tray cleanup and App.OnExit release the single-instance mutex.
            return null;
        }
        catch (OperationCanceledException) { return "Update preparation cancelled."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or CryptographicException)
        {
            return "The update was not started. " + error.Message;
        }
        finally
        {
            _updateInstallInProgress = false;
            MainInteractionSurface.IsEnabled = wasEnabled;
            _hotkeyManager.IsShortcutEditing = wasEditingShortcut;
        }
    }

    private void CloseSettingsPage(bool saved)
    {
        bool uninstall = _settingsPage?.UninstallRequested == true;
        ReturnToClipWorkspace();
        if (uninstall)
        {
            if (UpdateRequiresFinishingWork())
            {
                _vm.StatusDetails = "Stop recording and wait for clip saving and background work before uninstalling Flick.";
                return;
            }
            if (FlickUninstallLauncher.TryLaunch(out string error))
                RealQuit();
            else
                LocalClip.App.Services.FlickDialog.Show(this, error, "Flick Uninstall",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (!saved) return;
        _notificationService.StopSound();

        RegisterHotkeys();
        _vm.RefreshExportPreferences();
        UpdateSharePresetSelection();
        UpdateStatusBadgeColor();
        bool captureActive = _vm.IsCaptureActive;
        if (captureActive && (_settingsAudioSnapshot != AudioSettingsSnapshot() || (_settingsMicrophoneWasEnabled &&
            !_vm.Settings.EnableMicrophone)))
        {
            _ = StopCaptureForMicPrivacyAsync();
            return;
        }
        _vm.StatusDetails = captureActive
            ? "Preferences saved. New capture profile takes effect on the next manual replay start."
            : "Preferences saved. Replay remains off until manually started.";
    }

    private string AudioSettingsSnapshot() => System.Text.Json.JsonSerializer.Serialize(new {
        _vm.Settings.AudioMode, _vm.Settings.GameAudioSource, _vm.Settings.VoiceAudioSource,
        _vm.Settings.EnableMicrophone, _vm.Settings.MicrophoneDeviceId, _vm.Settings.SystemAudioDeviceId });

    private async System.Threading.Tasks.Task StopCaptureForMicPrivacyAsync()
    {
        try
        {
            if (_vm.IsRecordingDiagnosticRunning || _vm.DiagnosticHelperNeedsRestart)
            {
                _vm.StatusDetails = "Disabling the microphone and stopping the recording test…";
                bool exited = await _vm.CancelRecordingDiagnosticAndWaitAsync();
                _vm.StatusDetails = exited
                    ? "Microphone disabled and recording test stopped. Any produced original was kept."
                    : "Microphone preference is off, but test helper exit was not confirmed. Quit Flick to release its recording resources.";
                return;
            }
            await _vm.StopCaptureAsync();
            _vm.StatusDetails = "Audio preferences saved and capture stopped. Manually start replay when ready.";
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
        string? title = FlickDialog.Prompt("Rename Clip", "Enter a clip title:", current);
        if (title != null) _vm.RenameSelectedClip(title);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        _vm.DeleteSelectedClip();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FlickDialog.IsOpen) return;
        if (e.Key == Key.Escape && RecordingHealthPanel.Visibility == Visibility.Visible)
        {
            BtnCloseRecordingHealth_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (WorkspacePageLayer.Visibility == Visibility.Visible) return;
        if (e.Key == Key.Escape && EditorContextLayer.Visibility == Visibility.Visible)
        {
            CloseEditorContext();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None) return;
        // Space is a preview shortcut only when the user is pointing at the
        // player, never while typing in trim boxes or operating source controls.
        if (Keyboard.FocusedElement is TextBox ||
            Keyboard.FocusedElement is ComboBox ||
            Keyboard.FocusedElement is Slider) return;
        if (VideoPlayer.IsMouseOver || BtnPlayPause.IsKeyboardFocused)
        {
            BtnPlayPause_Click(BtnPlayPause, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void SuspendPlayback(bool clearSource)
    {
        if (clearSource) CancelPendingPreview();
        _playerTimer.Stop();
        if (VideoPlayer is null) return;
        try
        {
            VideoPlayer.Stop();
            if (clearSource)
            {
                VideoPlayer.Source = null;
                PlayerPoster.Visibility = _vm.SelectedClip?.IsAvailable == true
                    ? Visibility.Visible : Visibility.Collapsed;
                PlayerPosterHint.Visibility = PlayerPoster.Visibility;
            }
        }
        catch (Exception) { /* Safe display-only teardown. */ }
        _playerIsPlaying = false;
        if (BtnPlayPause != null) BtnPlayPause.Content = "Play";
    }

    private void ReleasePosterWhileHidden()
    {
        if (_posterCacheDetached) return;
        System.Windows.Data.BindingOperations.ClearBinding(
            PlayerPoster, System.Windows.Controls.Image.SourceProperty);
        PlayerPoster.Source = null; // Drop decoded poster pixels; keep clip model.
        _posterCacheDetached = true;
    }

    private void RestorePosterAfterShow()
    {
        if (!_posterCacheDetached) return;
        var converter = (System.Windows.Data.IValueConverter)FindResource("Flick.ThumbnailDecoder");
        System.Windows.Data.BindingOperations.SetBinding(
            PlayerPoster, System.Windows.Controls.Image.SourceProperty,
            new System.Windows.Data.Binding("SelectedClip.ThumbnailPath") { Converter = converter });
        _posterCacheDetached = false;
    }

    // Player/Editor Slice 4: WPF native preview first. An explicit, bounded
    // local MP4 can be created if this PC cannot decode the original MKV.
    private void CancelPendingPreview()
    {
        var pending = _previewCancellation;
        _previewCancellation = null;
        if (pending != null)
        {
            // The in-flight async method owns disposal in its finally block.
            try { pending.Cancel(); } catch (ObjectDisposedException) { }
        }
        if (BtnBuildPreview != null)
        {
            BtnBuildPreview.Content = "Create Local Preview";
            BtnBuildPreview.IsEnabled = _vm.SelectedClip?.IsAvailable == true;
        }
    }

    private void ResetPlayerForSelection()
    {
        CloseEditorContext();
        CancelPendingPreview();

        _playerTimer.Stop();
        _playerIsPlaying = false;
        VideoPlayer.Stop();
        VideoPlayer.Source = null;
        PlayerPoster.Visibility = Visibility.Collapsed;
        PlayerPosterHint.Visibility = Visibility.Collapsed;
        PlayerSlider.Value = 0;
        BtnPlayPause.Content = "Play";
        _playerView = PlayerView.Original;
        _previewWasReencoded = false;
        BtnPreviewMode.Content = "Original";

        var clip = _vm.SelectedClip;
        TrimTimeline.SetRange(clip?.DurationSeconds ?? 0,
            clip?.TrimStartSeconds ?? 0, clip?.TrimEndSeconds ?? 0);
        BtnPlayPause.IsEnabled = clip?.IsAvailable == true;
        BtnPreviewMode.IsEnabled = clip?.IsAvailable == true;
        BtnBuildPreview.IsEnabled = clip?.IsAvailable == true;
        TxtPlayerTime.Text = "0:00 / " + (clip is null ? "0:00" : clip.FormattedDuration);

        if (clip is null || !clip.IsAvailable)
        {
            PreviewFallbackPanel.Visibility = Visibility.Visible;
            TxtPreviewIssue.Text = clip is null
                ? "Select a Clip to Preview"
                : "Original Video Is Offline";
            BtnBuildPreview.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewFallbackPanel.Visibility = Visibility.Collapsed;
        BtnBuildPreview.Visibility = Visibility.Visible;
        PlayerPoster.Visibility = Visibility.Visible;
        PlayerPosterHint.Visibility = Visibility.Visible;
        // Important: assigning MediaElement.Source opens a decoder even without
        // Play(). Keep it null until playback is explicitly requested.
    }

    private void SetPreviewSource(string path, PlayerView mode, bool startPlaying)
    {
        if (!File.Exists(path))
        {
            _vm.StatusDetails = "Preview File Unavailable · Reconnect the Original";
            return;
        }
        _playerTimer.Stop();
        _playerIsPlaying = false;
        VideoPlayer.Stop();
        PlayerPoster.Visibility = Visibility.Collapsed;
        PlayerPosterHint.Visibility = Visibility.Collapsed;
        VideoPlayer.Source = new Uri(path);
        PlayerSlider.Value = 0;
        _playerView = mode;
        BtnPreviewMode.Content = mode switch
        {
            PlayerView.Exported => "Export Preview",
            PlayerView.LocalCopy => "Local Preview",
            PlayerView.AudioMix => "Mix Preview",
            _ => "Original"
        };
        BtnPlayPause.Content = "Play";
        PreviewFallbackPanel.Visibility = Visibility.Collapsed;
        if (startPlaying)
        {
            VideoPlayer.Play();
            _playerIsPlaying = true;
            BtnPlayPause.Content = "Pause";
            _playerTimer.Start();
        }
    }

    private void ShowOriginalPreview(bool startPlaying)
    {
        var clip = _vm.SelectedClip;
        if (clip is null || !File.Exists(clip.OriginalFilePath))
        {
            _vm.StatusDetails = "Original Video Offline · Reconnect Its Folder";
            return;
        }
        if (startPlaying)
            SetPreviewSource(clip.OriginalFilePath, PlayerView.Original, startPlaying: true);
        else
            ResetPlayerForSelection(); // Inactive editor retains the cheap poster.
        _vm.StatusDetails = "Original preview · recorded audio · use Preview Mix for current edits";
    }

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            var duration = VideoPlayer.NaturalDuration.TimeSpan;
            PlayerSlider.Maximum = Math.Max(.25, duration.TotalSeconds);
            TxtPlayerTime.Text = "0:00 / " + duration.ToString(
                duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        }
        PreviewFallbackPanel.Visibility = Visibility.Collapsed;
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        _playerTimer.Stop();
        _playerIsPlaying = false;
        VideoPlayer.Stop();
        PlayerSlider.Value = 0;
        BtnPlayPause.Content = "Play";
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedClip?.IsAvailable != true) return;
        if (_playerIsPlaying)
        {
            VideoPlayer.Pause();
            _playerIsPlaying = false;
            _playerTimer.Stop();
            BtnPlayPause.Content = "Play";
            return;
        }
        if (PreviewFallbackPanel.Visibility == Visibility.Visible) return;
        if (VideoPlayer.Source == null)
            SetPreviewSource(_vm.SelectedClip.OriginalFilePath, PlayerView.Original, false);
        if (VideoPlayer.Source == null) return;
        VideoPlayer.Play();
        _playerIsPlaying = true;
        _playerTimer.Start();
        BtnPlayPause.Content = "Pause";
    }

    private void CloseEditorTool_Click(object sender, RoutedEventArgs e) => CloseEditorContext();

    private void CloseEditorContext()
    {
        EditorContextLayer.Visibility = Visibility.Collapsed;
        TrimPanel.Visibility = AudioPanel.Visibility = SharePanel.Visibility = Visibility.Collapsed;
        AudioExpander.IsExpanded = false;
        if (_activeEditorTool != null)
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(_activeEditorTool, "Closed");
            _activeEditorTool.Focus();
        }
        _activeEditorTool = null;
    }

    private void PreviewSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Drawers scroll inside the existing preview allocation: no minimum
        // editor height that pushes transport/actions out of a compact window.
        EditorContextScroll.MaxHeight = Math.Max(32, PreviewSurface.ActualHeight - 100);
    }

    private void EditorTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tool } button) return;
        if (tool == "Original") { BtnPreviewMode_Click(sender, e); return; }
        var panel = tool switch { "Trim" => TrimPanel, "Audio" => AudioPanel, _ => SharePanel };
        bool open = panel.Visibility != Visibility.Visible;
        CloseEditorContext();
        if (!open) return;
        _activeEditorTool = button;
        panel.Visibility = Visibility.Visible;
        EditorContextLayer.Visibility = Visibility.Visible;
        if (tool == "Audio") AudioExpander.IsExpanded = true;
        System.Windows.Automation.AutomationProperties.SetHelpText(button, "Open");
        // Opening Share is navigation only; never starts export or posting.
        if (tool == "Share") UpdateSharePresetSelection();
        CloseEditorTool.Focus();
        EditorContextScroll.ScrollToTop();
    }

    private void BtnPreviewMode_Click(object sender, RoutedEventArgs e)
    {
        if (BtnPreviewMode.ContextMenu is null) return;
        BtnPreviewMode.ContextMenu.PlacementTarget = sender as Button ?? BtnPreviewMode;
        BtnPreviewMode.ContextMenu.IsOpen = true;
    }

    private void PreviewExport_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsExportReady || !File.Exists(_vm.ReadyExportFilePath))
        {
            _vm.StatusDetails = "Export this clip first to preview its final audio mix.";
            return;
        }
        CancelPendingPreview();
        SetPreviewSource(_vm.ReadyExportFilePath, PlayerView.Exported, true);
        _vm.StatusDetails = "Export Preview · H.264/AAC MP4 with the selected audio mix";
    }

    private void PreviewOriginal_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingPreview();
        ShowOriginalPreview(startPlaying: true);
    }

    private void PlayerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider)
        {
            double seconds = Math.Clamp(PlayerSlider.Value, 0,
                Math.Max(0, PlayerSlider.Maximum));
            TxtPlayerTime.Text = TimeSpan.FromSeconds(seconds).ToString(@"m\:ss") +
                " / " + (_vm.SelectedClip?.FormattedDuration ?? "0:00");
        }
    }

    private void PlayerSlider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;
        double seconds = PlayerSlider.Value;
        double amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 5 : 1;
        switch (e.Key)
        {
            case Key.Left: seconds -= amount; break;
            case Key.Right: seconds += amount; break;
            case Key.Home: seconds = 0; break;
            case Key.End: seconds = PlayerSlider.Maximum; break;
            default: return;
        }
        PlayerSlider.Value = Math.Clamp(seconds, 0, PlayerSlider.Maximum);
        VideoPlayer.Position = TimeSpan.FromSeconds(PlayerSlider.Value);
        e.Handled = true;
    }

    private void PlayerTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDraggingSlider || !VideoPlayer.NaturalDuration.HasTimeSpan) return;
        PlayerSlider.Value = VideoPlayer.Position.TotalSeconds;
        var position = VideoPlayer.Position;
        var length = VideoPlayer.NaturalDuration.TimeSpan;
        TxtPlayerTime.Text = position.ToString(position.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") +
                             " / " + length.ToString(length.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
    }

    private void PlayerSlider_MouseDown(object sender, MouseButtonEventArgs e) =>
        _isDraggingSlider = true;

    private void PlayerSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
            VideoPlayer.Position = TimeSpan.FromSeconds(
                Math.Clamp(PlayerSlider.Value, 0, PlayerSlider.Maximum));
        _isDraggingSlider = false;
    }

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _playerTimer.Stop();
        _playerIsPlaying = false;
        BtnPlayPause.Content = "Play";
        PreviewFallbackPanel.Visibility = Visibility.Visible;
        BtnBuildPreview.Visibility = _playerView == PlayerView.Original ||
            _playerView == PlayerView.LocalCopy && !_previewWasReencoded
            ? Visibility.Visible : Visibility.Collapsed;
        BtnBuildPreview.Content = _playerView == PlayerView.LocalCopy
            ? "Try Compatible Preview" : "Create Local Preview";
        TxtPreviewIssue.Text = _playerView switch {
            PlayerView.Exported => "Windows Couldn't Preview the Exported MP4",
            PlayerView.AudioMix => "Windows Couldn't Play the Audio Mix Preview",
            _ => "Windows Couldn't Play This Original" };
        _vm.StatusDetails = _playerView switch {
            PlayerView.Exported => "Export preview unavailable. Open the validated MP4 externally.",
            PlayerView.AudioMix => "Mix playback unavailable. The original remains unchanged; export the mix to hear it in another player.",
            _ => "Original MKV playback unsupported by Windows. Create a temporary local MP4 or open the original in another player." };
    }

    private async void BuildPlayablePreview_Click(object sender, RoutedEventArgs e)
    {
        var clip = _vm.SelectedClip;
        if (clip?.IsAvailable != true || _vm.IsCaptureActive || _vm.IsExporting)
        {
            _vm.StatusDetails = "Finish live capture or export before creating a local preview.";
            return;
        }
        bool forceTranscode = _playerView == PlayerView.LocalCopy;
        CancelPendingPreview();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        BtnBuildPreview.IsEnabled = false;
        BtnBuildPreview.Content = "Preparing…";
        _vm.StatusDetails = "Preparing a bounded, on-demand local playback copy…";
        try
        {
            _localPreviewProber ??= new MediaProber();
            string result = await _localPreviewProber.BuildPlaybackPreviewAsync(
                clip.OriginalFilePath, cancellation.Token, forceTranscode);
            if (cancellation.IsCancellationRequested || _vm.SelectedClip?.Id != clip.Id)
                return;
            _previewWasReencoded = forceTranscode;
            SetPreviewSource(result, PlayerView.LocalCopy, startPlaying: true);
            _vm.StatusDetails = clip.DurationSeconds > 180
                ? "Local preview covers the first 3 minutes. Your original remains untouched."
                : "Playing local compatibility copy. Your original remains untouched.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_vm.SelectedClip?.Id == clip.Id)
            {
                _vm.StatusDetails = "Couldn't Create Local Preview: " + ex.Message;
                PreviewFallbackPanel.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                _previewCancellation = null;
                BtnBuildPreview.IsEnabled = true;
                BtnBuildPreview.Content = "Create Local Preview";
            }
            cancellation.Dispose();
        }
    }

    private void OpenInPlayer_Click(object sender, RoutedEventArgs e)
    {
        string? path = _playerView == PlayerView.Exported &&
                       File.Exists(_vm.ReadyExportFilePath)
            ? _vm.ReadyExportFilePath : _vm.SelectedClip?.OriginalFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _vm.StatusDetails = "Original Video Offline · Reconnect Its Folder";
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { _vm.StatusDetails = "Could Not Open External Player: " + ex.Message; }
    }

    private void BtnClipActions_Click(object sender, RoutedEventArgs e)
    {
        if (BtnClipActions.ContextMenu is null) return;
        BtnClipActions.ContextMenu.PlacementTarget = BtnClipActions;
        BtnClipActions.ContextMenu.IsOpen = true;
    }

    private void TrimTimeline_RangeCommitted(object sender, TrimRangeCommittedEventArgs e)
    {
        if (!_vm.ApplyTrim(e.Start, e.End))
        {
            var clip = _vm.SelectedClip;
            TrimTimeline.SetRange(clip?.DurationSeconds ?? 0,
                clip?.TrimStartSeconds ?? 0, clip?.TrimEndSeconds ?? 0);
            return;
        }
        if (_playerView == PlayerView.Exported)
            ShowOriginalPreview(startPlaying: false);
    }

    private void ResetAudioMix_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetAudioMix();
        if (_playerView == PlayerView.Exported)
            ShowOriginalPreview(startPlaying: false);
    }

    // Share UI Slice 5: one size chooser, optional custom field, and real MP4.
    private void ExportCap_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || _updatingExportCap || CmbExportCap is null ||
            CustomCapPanel is null || TxtCustomMb is null) return;
        bool custom = CmbExportCap.SelectedIndex == 4;
        CustomCapPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (custom)
        {
            TxtCustomMb.Text = _vm.TargetSizeMb.ToString();
            TxtCustomMb.SelectAll();
            return; // Selecting Custom does not silently change a saved cap.
        }
        int selected = CmbExportCap.SelectedIndex switch
        {
            0 => 10,
            1 => 20,
            2 => 50,
            3 => 100,
            _ => _vm.TargetSizeMb
        };
        ApplyExportSize(selected);
    }

    private void ApplyExportSize(int cap)
    {
        if (_vm.IsExporting)
        {
            _vm.StatusDetails = "Finish or Cancel the Current Export Before Changing Its Limit.";
            UpdateSharePresetSelection();
            return;
        }
        try
        {
            _vm.TargetSizeMb = cap;
            UpdateSharePresetSelection();
        }
        catch (Exception ex)
        {
            _vm.StatusDetails = "Couldn't Save File Limit: " + ex.Message;
            UpdateSharePresetSelection();
        }
    }

    private void TxtCustomMb_LostFocus(object sender, RoutedEventArgs e)
    {
        if (CmbExportCap.SelectedIndex == 4) TryApplyCustomSize();
    }

    private void CustomMb_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryApplyCustomSize();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TxtCustomMb.Text = _vm.TargetSizeMb.ToString();
            e.Handled = true;
        }
    }

    private void TryApplyCustomSize()
    {
        if (!int.TryParse(TxtCustomMb.Text, out var cap) ||
            !ShareExportTargets.IsValid(cap))
        {
            _vm.StatusDetails = "Custom Export Limit Must Be Between 1 and 250 MB.";
            TxtCustomMb.Text = _vm.TargetSizeMb.ToString();
            return;
        }
        ApplyExportSize(cap);
    }

    private void UpdateSharePresetSelection()
    {
        _updatingExportCap = true;
        try
        {
            int cap = _vm.TargetSizeMb;
            CmbExportCap.SelectedIndex = cap switch
            {
                10 => 0, 20 => 1, 50 => 2, 100 => 3, _ => 4
            };
            CustomCapPanel.Visibility = CmbExportCap.SelectedIndex == 4
                ? Visibility.Visible : Visibility.Collapsed;
            TxtCustomMb.Text = cap.ToString();
            CmbExportCap.IsEnabled = !_vm.IsExporting;
            TxtCustomMb.IsEnabled = !_vm.IsExporting;
        }
        finally { _updatingExportCap = false; }
    }

    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingPreview();
        SuspendPlayback(clearSource: true);
        var operation = _vm.ExportForDiscordAsync();
        UpdateSharePresetSelection(); // Disable size switching as soon as export starts.
        await operation;
        UpdateSharePresetSelection();
    }

    private void BtnCancelExport_Click(object sender, RoutedEventArgs e)
    {
        _vm.CancelExport();
        _vm.ExportStatusMessage = "Cancelling Local Export…";
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _shareDragStart = _vm.TryGetReadyShareFile(out _)
            ? e.GetPosition(DragHandle) : null;
    }

    private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _shareDragStart is null)
        {
            _shareDragStart = null;
            return;
        }
        System.Windows.Point pos = e.GetPosition(DragHandle);
        System.Windows.Point from = _shareDragStart.Value;
        if (Math.Abs(pos.X - from.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - from.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _shareDragStart = null;
        if (!_vm.TryGetReadyShareFile(out var path))
        {
            _vm.ExportStatusMessage = "Share File No Longer Valid · Export This Clip Again";
            return;
        }
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        DragDrop.DoDragDrop(DragHandle, data, DragDropEffects.Copy);
    }

    private void BtnCopyFile_Click(object sender, RoutedEventArgs e) =>
        _vm.CopyExportedFileToClipboard();

    private void BtnOpenExportFolder_Click(object sender, RoutedEventArgs e) =>
        _vm.OpenExportFolder();

    private async void BtnSendWebhook_Click(object sender, RoutedEventArgs e)
    {
        await _vm.SendToDiscordWebhookAsync();
    }
}
