using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Sharing;
using LocalClip.Core.Storage;

namespace LocalClip.App.ViewModels;

public class TargetItem
{
    public string Type { get; set; } = "Monitor"; // "Monitor" or "Window"
    public int MonitorIndex { get; set; }
    public ulong WindowHwnd { get; set; }
    public string DisplayName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Exe { get; set; } = "";

    public override string ToString() => DisplayName;
}

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettings _settings;
    private readonly CaptureHostClient _captureHost;
    private readonly ClipStorage _storage;
    private readonly MediaProber _prober;
    private readonly ClipExporter _exporter;
    private readonly ResourceMonitor _resourceMonitor;
    private readonly DispatcherTimer _pollTimer;

    // Capture state
    private string _captureStatus = "Off";
    private string _bufferedDurationText = "0.0s / 30.0s";
    private bool _isArmed = false;
    private string _statusDetails = "";
    private string _resourceStatsText = "Memory: -- | CPU: --";

    // Targets
    private ObservableCollection<TargetItem> _targets = new();
    private TargetItem? _selectedTarget;

    // Clips
    private ObservableCollection<ClipMetadata> _allClips = new();
    private ObservableCollection<ClipMetadata> _filteredClips = new();
    private ClipMetadata? _selectedClip;
    private string _searchQuery = "";

    // Export & Sharing
    private int _targetSizeMb = 20;
    private bool _isExporting = false;
    private double _exportProgress = 0.0;
    private string _exportStatusMessage = "";
    private bool _isExportReady = false;
    private string _readyExportFilePath = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    // Fired ONLY after the saved original is validated and indexed.
    public event Action<ClipMetadata>? ClipSavedConfirmed;
    public event Action<string>? ReplaySaveFailed;

    public AppSettings Settings => _settings;
    public CaptureHostClient CaptureHost => _captureHost;

    public string CaptureStatus
    {
        get => _captureStatus;
        set { _captureStatus = value; OnPropertyChanged(); }
    }

    public string BufferedDurationText
    {
        get => _bufferedDurationText;
        set { _bufferedDurationText = value; OnPropertyChanged(); }
    }

    public bool IsArmed
    {
        get => _isArmed;
        set { _isArmed = value; OnPropertyChanged(); }
    }

    public string StatusDetails
    {
        get => _statusDetails;
        set { _statusDetails = value; OnPropertyChanged(); }
    }

    public string ResourceStatsText
    {
        get => _resourceStatsText;
        set { _resourceStatsText = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TargetItem> Targets
    {
        get => _targets;
        set { _targets = value; OnPropertyChanged(); }
    }

    public TargetItem? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (_selectedTarget != value)
            {
                _selectedTarget = value;
                OnPropertyChanged();
                OnTargetChanged();
            }
        }
    }

    public ObservableCollection<ClipMetadata> FilteredClips
    {
        get => _filteredClips;
        set { _filteredClips = value; OnPropertyChanged(); }
    }

    public ClipMetadata? SelectedClip
    {
        get => _selectedClip;
        set
        {
            if (_selectedClip != value)
            {
                _selectedClip = value;
                OnPropertyChanged();
                OnSelectedClipChanged();
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            _searchQuery = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public int TargetSizeMb
    {
        get => _targetSizeMb;
        set
        {
            if (value < 1 || value > 250) return;
            _targetSizeMb = value;
            _settings.DiscordTargetSizeMb = value;
            _settings.Save();
            OnPropertyChanged();
            InvalidateExportReady();
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        set { _isExporting = value; OnPropertyChanged(); }
    }

    public double ExportProgress
    {
        get => _exportProgress;
        set { _exportProgress = value; OnPropertyChanged(); }
    }

    public string ExportStatusMessage
    {
        get => _exportStatusMessage;
        set { _exportStatusMessage = value; OnPropertyChanged(); }
    }

    public bool IsExportReady
    {
        get => _isExportReady;
        set { _isExportReady = value; OnPropertyChanged(); }
    }

    public string ReadyExportFilePath
    {
        get => _readyExportFilePath;
        set { _readyExportFilePath = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _captureHost = new CaptureHostClient();
        _prober = new MediaProber();
        _storage = new ClipStorage(_settings, _prober);
        _exporter = new ClipExporter(prober: _prober);
        _resourceMonitor = new ResourceMonitor();

        _targetSizeMb = _settings.DiscordTargetSizeMb;

        // Wire IPC events
        _captureHost.StateChanged += OnHostStateChanged;
        _captureHost.ReplaySavedWithTracks += OnHostReplaySaved;
        _captureHost.FaultOccurred += OnHostFaultOccurred;
        _captureHost.EventReceived += OnHostEventReceived;

        // Status polling timer
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        // Load library
        ReloadClips();
    }

    public async Task InitializeAsync()
    {
        await RefreshTargetsAsync();
    }

    public async Task RefreshTargetsAsync()
    {
        try
        {
            var targets = await _captureHost.GetTargetsAsync();
            Targets.Clear();

            foreach (var m in targets.Monitors)
            {
                Targets.Add(new TargetItem
                {
                    Type = "Monitor",
                    MonitorIndex = m.Index,
                    DisplayName = m.DisplayName
                });
            }

            foreach (var w in targets.Windows)
            {
                Targets.Add(new TargetItem
                {
                    Type = "Window",
                    WindowHwnd = w.Hwnd,
                    DisplayName = w.DisplayName,
                    Title = w.Title,
                    Exe = w.Exe
                });
            }

            if (Targets.Count > 0 && SelectedTarget == null)
            {
                SelectedTarget = Targets[0];
            }
        }
        catch (Exception ex)
        {
            StatusDetails = "Target refresh error: " + ex.Message;
        }
        finally
        {
            // Target discovery must not leave an idle native helper running.
            if (CaptureStatus == "Off")
                await _captureHost.ShutdownAsync();
        }
    }

    private void OnTargetChanged()
    {
        if (SelectedTarget == null) return;

        if (SelectedTarget.Type == "Monitor")
        {
            _settings.TargetType = "Monitor";
            _settings.MonitorIndex = SelectedTarget.MonitorIndex;
            _settings.WindowHwnd = 0;
            _settings.WindowTitle = "";
        }
        else
        {
            _settings.TargetType = "Window";
            _settings.WindowHwnd = SelectedTarget.WindowHwnd;
            _settings.WindowTitle = SelectedTarget.Title;
            _settings.WindowExecutable = SelectedTarget.Exe;
        }
        _settings.Save();

        // If currently capturing, explain that target change resets buffer
        if (CaptureStatus != "Off")
        {
            StatusDetails = "Target changed: Capture stopped to reset replay buffer.";
            _ = StopCaptureAsync();
        }
    }

    public async Task ToggleCaptureAsync()
    {
        if (CaptureStatus == "Starting" || CaptureStatus == "Stopping") return;
        if (CaptureStatus == "Off" || CaptureStatus == "Faulted" || CaptureStatus == "Paused")
        {
            await StartCaptureAsync();
        }
        else
        {
            await StopCaptureAsync();
        }
    }

    public async Task StartCaptureAsync()
    {
        if (CaptureStatus == "Starting" || CaptureStatus == "Stopping") return;
        CaptureStatus = "Starting";
        StatusDetails = "Initializing capture encoder and audio...";
        try
        {
            if (!await _captureHost.StartCaptureAsync(_settings))
            {
                CaptureStatus = "Faulted";
                StatusDetails = "Capture host refused to start. Check target, encoder and logs.";
            }
        }
        catch (Exception ex)
        {
            CaptureStatus = "Faulted";
            StatusDetails = "Capture startup failed: " + ex.Message;
        }
    }

    public async Task StopCaptureAsync()
    {
        if (CaptureStatus == "Stopping") return;
        CaptureStatus = "Stopping";
        bool stopped = false;
        try { stopped = await _captureHost.StopCaptureAsync(); }
        catch (Exception ex) { StatusDetails = "Capture stop error: " + ex.Message; }
        finally
        {
            await _captureHost.ShutdownAsync();
            CaptureStatus = "Off";
            IsArmed = false;
            BufferedDurationText = $"0.0s / {_settings.ReplayDurationSeconds}.0s";
            StatusDetails = stopped ? "Capture stopped; native helper exited." :
                "Capture was force-terminated. Check the previous clip before restarting.";
        }
    }

    public async Task SaveReplayAsync()
    {
        if (CaptureStatus != "Buffering" && CaptureStatus != "Armed")
        {
            StatusDetails = "Turn replay capture on before saving a clip.";
            return;
        }

        StatusDetails = "Snapshotting replay buffer to original MKV...";
        string saveId = "clip_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N");
        try
        {
            if (!await _captureHost.SaveReplayAsync(saveId, "Clip"))
            {
                StatusDetails = "Failed to trigger replay save.";
                ReplaySaveFailed?.Invoke(StatusDetails);
            }
        }
        catch (Exception ex)
        {
            StatusDetails = "Replay save failed: " + ex.Message;
            ReplaySaveFailed?.Invoke(StatusDetails);
        }
    }

    private void OnHostStateChanged(string state)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CaptureStatus = state;
            if (state == "Armed")
            {
                IsArmed = true;
                StatusDetails = "Ready to Save: Full replay buffer available.";
            }
            else if (state == "Buffering")
            {
                IsArmed = false;
                StatusDetails = "Buffering gameplay frames...";
            }
            else if (state == "Off")
            {
                IsArmed = false;
            }
        });
    }

    private void OnHostReplaySaved(string saveId, string filePath, System.Collections.Generic.IReadOnlyList<string> trackNames)
    {
        Application.Current?.Dispatcher.Invoke(async () =>
        {
            StatusDetails = $"Saved original clip: {Path.GetFileName(filePath)}";
            if (File.Exists(filePath))
            {
                try
                {
                    string targetName = SelectedTarget?.DisplayName ?? "Screen";
                    var clip = await _storage.RegisterNewOriginalAsync(filePath, targetName, trackNames);
                    ReloadClips();
                    // A hotkey save must not interrupt somebody editing another clip.
                    if (SelectedClip is null)
                        SelectedClip = clip;
                    ClipSavedConfirmed?.Invoke(clip);
                }
                catch (Exception ex)
                {
                    StatusDetails = "Saved clip could not be indexed: " + ex.Message;
                    ReplaySaveFailed?.Invoke(StatusDetails);
                }
            }
        });
    }

    private void OnHostEventReceived(string eventName, System.Text.Json.JsonElement? data)
    {
        if (eventName != "save_failed") return;
        string reason = "Unable to save the current replay";
        if (data.HasValue && data.Value.TryGetProperty("reason", out var value))
            reason = value.GetString() ?? reason;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null)
            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                StatusDetails = "Replay save failed: " + reason;
                ReplaySaveFailed?.Invoke(reason);
            }));
        // Save errors leave an otherwise healthy replay buffer armed.
    }

    private void OnHostFaultOccurred(string reason)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CaptureStatus = "Faulted";
            IsArmed = false;
            StatusDetails = "Capture Fault: " + reason;
        });
    }

    private async void OnPollTick(object? sender, EventArgs e)
    {
        if (_captureHost.IsRunning)
        {
            try
            {
                var st = await _captureHost.GetStatusAsync();
                if (st.State == "Buffering" || st.State == "Armed")
                {
                    BufferedDurationText = $"~{st.BufferedSec:F1}s / {st.MaxSec:F0}s";
                }

                if (!st.TargetValid && CaptureStatus != "Faulted")
                {
                    CaptureStatus = "Faulted";
                    StatusDetails = "Selected target window was closed or lost.";
                }

                // Sample resource usage
                var sample = _resourceMonitor.Sample();
                double hostMb = st.PrivateBytes / (1024.0 * 1024.0);
                double appMb = sample.AppPrivateBytes / (1024.0 * 1024.0);
                ResourceStatsText = $"App RAM: {appMb:F0} MB | Host RAM: {hostMb:F0} MB | CPU: {sample.AppCpuPercent:F1}%";

                if (st.PrivateBytes > 805_306_368L)
                {
                    await StopCaptureAsync();
                    StatusDetails = "Capture stopped: native host exceeded the 768 MiB safety limit.";
                    return;
                }
                if (sample.IsLowSystemMemory)
                {
                    StatusDetails = "Warning: System available physical memory < 1 GB!";
                }
            }
            catch { }
        }
    }

    public async Task RescanLibraryAsync()
    {
        await _storage.RescanMediaFolderAsync(_settings.OriginalsFolder);
        ReloadClips();
    }

    public void SaveClipEdits()
    {
        if (SelectedClip == null) return;
        InvalidateExportReady();
        _storage.AddOrUpdateClip(SelectedClip);
    }

    public void ReloadClips()
    {
        _allClips.Clear();
        foreach (var c in _storage.GetAllClips())
        {
            _allClips.Add(c);
        }
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        FilteredClips.Clear();
        var q = _searchQuery.Trim().ToLowerInvariant();
        foreach (var c in _allClips)
        {
            if (string.IsNullOrEmpty(q) ||
                c.Title.ToLowerInvariant().Contains(q) ||
                c.TargetName.ToLowerInvariant().Contains(q))
            {
                FilteredClips.Add(c);
            }
        }
    }

    private void OnSelectedClipChanged()
    {
        if (SelectedClip == null)
        {
            InvalidateExportReady();
            return;
        }

        // Check if existing export matches current state
        string fp = ExportFingerprint.Compute(
            SelectedClip.OriginalFilePath,
            SelectedClip.TrimStartSeconds,
            SelectedClip.TrimEndSeconds > 0 ? SelectedClip.TrimEndSeconds : SelectedClip.DurationSeconds,
            SelectedClip.AudioTracks,
            TargetSizeMb
        );

        if (SelectedClip.IsExportReady &&
            SelectedClip.LastExportFingerprint == fp &&
            File.Exists(SelectedClip.ExportedFilePath))
        {
            IsExportReady = true;
            ReadyExportFilePath = SelectedClip.ExportedFilePath;
            ExportStatusMessage = $"Ready: {Path.GetFileName(ReadyExportFilePath)} ({SelectedClip.ExportedFileSizeBytes / 1_000_000.0:F2} MB)";
        }
        else
        {
            InvalidateExportReady();
        }
    }

    public void InvalidateExportReady()
    {
        IsExportReady = false;
        ReadyExportFilePath = "";
        ExportStatusMessage = "Clip edited: Click 'Export for Discord' to create share file.";
        if (SelectedClip != null)
        {
            SelectedClip.IsExportReady = false;
        }
    }

    public async Task ExportForDiscordAsync()
    {
        if (SelectedClip == null || IsExporting) return;

        IsExporting = true;
        ExportProgress = 0.0;
        ExportStatusMessage = "Starting Discord export...";

        var progress = new Progress<ExportProgress>(p =>
        {
            ExportProgress = p.Percent;
            ExportStatusMessage = p.Stage;
        });

        _settings.DiscordTargetSizeMb = TargetSizeMb;

        ExportResult result;
        try
        {
            result = await _exporter.ExportForDiscordAsync(SelectedClip, _settings, progress);
        }
        catch (Exception ex)
        {
            result = new ExportResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            IsExporting = false;
        }

        if (result.Success)
        {
            IsExportReady = true;
            ReadyExportFilePath = result.ExportFilePath;
            SelectedClip.ExportedFilePath = result.ExportFilePath;
            SelectedClip.ExportedFileSizeBytes = result.FileSizeBytes;
            SelectedClip.LastExportFingerprint = result.Fingerprint;
            SelectedClip.IsExportReady = true;
            _storage.AddOrUpdateClip(SelectedClip);

            double mb = result.FileSizeBytes / 1_000_000.0;
            ExportStatusMessage = $"Ready to Share! ({mb:F2} MB, below {TargetSizeMb} MB cap)";
        }
        else
        {
            IsExportReady = false;
            ExportStatusMessage = "Export failed: " + result.ErrorMessage;
            MessageBox.Show(result.ErrorMessage, "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void CopyExportedFileToClipboard()
    {
        if (!File.Exists(ReadyExportFilePath)) return;
        try
        {
            var files = new System.Collections.Specialized.StringCollection { ReadyExportFilePath };
            Clipboard.SetFileDropList(files);
            ExportStatusMessage = "Copied file to clipboard! Paste directly into Discord.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to copy file: " + ex.Message, "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void OpenExportFolder()
    {
        string path = File.Exists(ReadyExportFilePath) ? ReadyExportFilePath : SelectedClip?.OriginalFilePath ?? _settings.OriginalsFolder;
        if (File.Exists(path))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else if (Directory.Exists(_settings.OriginalsFolder))
        {
            System.Diagnostics.Process.Start("explorer.exe", _settings.OriginalsFolder);
        }
    }

    public void DeleteSelectedClip()
    {
        if (SelectedClip == null) return;
        var r = MessageBox.Show($"Are you sure you want to delete '{SelectedClip.Title}'? The file will be sent to the Recycle Bin.", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
        {
            string id = SelectedClip.Id;
            _storage.DeleteClip(id);
            ReloadClips();
            SelectedClip = FilteredClips.FirstOrDefault();
        }
    }

    public void RenameSelectedClip(string newTitle)
    {
        if (SelectedClip == null || string.IsNullOrWhiteSpace(newTitle)) return;
        _storage.RenameClip(SelectedClip.Id, newTitle.Trim());
        ReloadClips();
    }

    public async Task SendToDiscordWebhookAsync(string message = "")
    {
        if (!File.Exists(ReadyExportFilePath))
        {
            MessageBox.Show("Please export the clip first before sending.", "No Ready File", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string secretUrl = DiscordWebhookClient.UnprotectSecret(_settings.MaskedWebhookUrl);
        if (string.IsNullOrEmpty(secretUrl))
        {
            MessageBox.Show("No Discord Webhook URL is configured. Open Settings to set one up.", "Webhook Not Configured", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var r = MessageBox.Show($"Send this clip ({Path.GetFileName(ReadyExportFilePath)}) to channel '{_settings.DiscordWebhookChannelName}'?", "Confirm Send to Discord", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        ExportStatusMessage = "Posting clip to Discord webhook...";
        var client = new DiscordWebhookClient();
        var res = await client.PostClipAsync(secretUrl, ReadyExportFilePath, message);

        if (res.Success)
        {
            ExportStatusMessage = "Successfully posted to Discord!";
            MessageBox.Show("Clip posted successfully!", "Discord Webhook", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            ExportStatusMessage = "Post failed: " + res.ErrorMessage;
            MessageBox.Show(res.ErrorMessage, "Discord Webhook Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _captureHost.Dispose();
        GC.SuppressFinalize(this);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
