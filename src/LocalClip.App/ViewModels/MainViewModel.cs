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
    // Audio UI changes are reflected instantly but index writes are debounced.
    private readonly DispatcherTimer _audioEditCommitTimer;
    private ClipMetadata? _pendingAudioClip;
    private readonly System.Collections.Generic.List<AudioTrackInfo> _subscribedAudioTracks = new();

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
    private System.Collections.Generic.List<ClipMetadata> _allClips = new();
    private LibraryObservableCollection<ClipMetadata> _filteredClips = new();
    private ClipMetadata? _selectedClip;
    private string _searchQuery = "";
    private string _clipSourceFilter = "All Clips";
    private string _librarySummaryText = "0 Clips";
    private string _importStatusText = "";
    private bool _isImporting;
    private CancellationTokenSource? _importCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private readonly SemaphoreSlim _thumbnailGate = new(1, 1);
    private readonly ObservableCollection<ConnectedClipFolder> _connectedFolders = new();

    // Export & Sharing
    private int _targetSizeMb = 20;
    private bool _isExporting = false;
    private double _exportProgress = 0.0;
    private string _exportStatusMessage = "";
    private bool _isExportReady = false;
    private string _readyExportFilePath = "";
    private CancellationTokenSource? _exportCancellation;
    private int _exportGeneration;

    public event PropertyChangedEventHandler? PropertyChanged;
    // Fired ONLY after the saved original is validated and indexed.
    public event Action<ClipMetadata>? ClipSavedConfirmed;
    public event Action<string>? ReplaySaveFailed;

    public AppSettings Settings => _settings;
    public bool IsCaptureActive => CaptureActive;
    public bool CanEditClip => SelectedClip?.IsAvailable == true && !IsExporting;
    public bool CanStartExport => CanEditClip && !IsImporting;
    // WPF shell supplies consent before an active source change discards replay.
    public Func<bool>? ConfirmTargetReset { get; set; }
    public CaptureHostClient CaptureHost => _captureHost;
    public ObservableCollection<ConnectedClipFolder> ConnectedFolders => _connectedFolders;

    public string ClipSourceFilter
    {
        get => _clipSourceFilter;
        set
        {
            if (value != "All Clips" && value != "Flick" && value != "Imported") return;
            if (_clipSourceFilter == value) return;
            _clipSourceFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public string LibrarySummaryText
    {
        get => _librarySummaryText;
        private set { _librarySummaryText = value; OnPropertyChanged(); }
    }

    public string ImportStatusText
    {
        get => _importStatusText;
        private set { _importStatusText = value; OnPropertyChanged(); }
    }

    public bool IsImporting
    {
        get => _isImporting;
        private set { _isImporting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartExport)); }
    }

    private bool CaptureActive => CaptureStatus is "Starting" or "Buffering" or "Armed" or "Stopping";

    public void RefreshConnectedFolders()
    {
        _connectedFolders.Clear();
        foreach (var folder in _storage.GetConnectedFolders())
            _connectedFolders.Add(folder);
        UpdateLibrarySummary();
    }

    private void UpdateLibrarySummary()
    {
        int all = _storage.GetAllClips().Count;
        int folders = _connectedFolders.Count;
        LibrarySummaryText = $"{all} {(all == 1 ? "Clip" : "Clips")} · " +
                             $"{folders} {(folders == 1 ? "Folder" : "Folders")}";
    }

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
                bool active = CaptureStatus is "Starting" or "Buffering" or "Armed" or "Stopping";
                if (_selectedTarget != null && active)
                {
                    bool allowed = CaptureStatus is not ("Starting" or "Stopping")
                        && ConfirmTargetReset?.Invoke() == true;
                    if (!allowed)
                    {
                        // The ComboBox's requested item is rejected; restore the
                        // old selection without touching source settings or buffer.
                        OnPropertyChanged();
                        return;
                    }
                }
                _selectedTarget = value;
                OnPropertyChanged();
                OnTargetChanged();
            }
        }
    }

    public ObservableCollection<ClipMetadata> FilteredClips => _filteredClips;

    public ClipMetadata? SelectedClip
    {
        get => _selectedClip;
        set
        {
            if (_selectedClip != value)
            {
                FlushPendingAudioEdits();
                UnsubscribeAudioEvents();
                _selectedClip = value;
                SubscribeAudioEvents();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanEditClip));
                OnPropertyChanged(nameof(CanStartExport));
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
            if (!ShareExportTargets.IsValid(value) || _targetSizeMb == value) return;
            _settings.DiscordTargetSizeMb = value;
            _settings.Save();
            _targetSizeMb = value;
            OnPropertyChanged();
            OnSelectedClipChanged();
        }
    }

    public void RefreshExportPreferences()
    {
        int value = ShareExportTargets.IsValid(_settings.DiscordTargetSizeMb)
            ? _settings.DiscordTargetSizeMb : ShareExportTargets.DefaultMb;
        if (_targetSizeMb != value)
        {
            _targetSizeMb = value;
            OnPropertyChanged(nameof(TargetSizeMb));
        }
        OnSelectedClipChanged();
    }

    public bool TryGetReadyShareFile(out string path)
    {
        path = "";
        return !IsExporting && IsExportReady &&
               ReadyShareFile.TryGet(SelectedClip, TargetSizeMb, out path) &&
               string.Equals(path, ReadyExportFilePath, StringComparison.OrdinalIgnoreCase);
    }

    public void CancelExport() => _exportCancellation?.Cancel();

    public bool IsExporting
    {
        get => _isExporting;
        set { _isExporting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditClip)); OnPropertyChanged(nameof(CanStartExport)); }
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
        _audioEditCommitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _audioEditCommitTimer.Tick += (_, _) => FlushPendingAudioEdits();

        // Load library and user-authorized linked sources (no scanning on startup).
        RefreshConnectedFolders();
        ReloadClips();
        if (!string.IsNullOrWhiteSpace(_storage.LastLoadWarning))
            StatusDetails = _storage.LastLoadWarning;
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
        // Never let folder indexing compete with the live gameplay encoder.
        _importCancellation?.Cancel();
        _thumbnailCancellation?.Cancel();
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
                    // Reloading an ObservableCollection can temporarily clear the
                    // ListBox selection through its TwoWay binding. Snapshot the
                    // edit context BEFORE rebuilding the gallery.
                    var editing = SelectedClip;
                    ReloadClips();
                    SelectedClip = editing is null
                        ? clip
                        : FilteredClips.FirstOrDefault(c => c.Id == editing.Id) ?? editing;
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

    public async Task<ClipImportSummary> ImportFolderAsync(
        string folderPath, bool includeSubfolders = true)
    {
        if (CaptureActive || IsExporting)
            throw new InvalidOperationException("Finish the active replay or export before importing. Gameplay and sharing take priority.");
        if (IsImporting)
            throw new InvalidOperationException("An import is already in progress.");
        var folder = _storage.ConnectFolder(folderPath, includeSubfolders);
        RefreshConnectedFolders();
        return await RescanConnectedFolderAsync(folder.Id);
    }

    public async Task<ClipImportSummary> RescanConnectedFolderAsync(string folderId)
    {
        if (CaptureActive || IsExporting)
            throw new InvalidOperationException("Finish the active replay or export before scanning external clips.");
        if (IsImporting)
            throw new InvalidOperationException("A folder is already being scanned.");

        using var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;
        IsImporting = true;
        ImportStatusText = "Finding Videos…";
        var progress = new Progress<ClipImportProgress>(p =>
        {
            ImportStatusText = $"Checking {p.Examined:N0} of {p.TotalDiscovered:N0} Videos · " +
                               $"{p.Imported:N0} Added · {p.Failed:N0} Unavailable";
        });
        try
        {
            var result = await _storage.ScanConnectedFolderAsync(
                folderId, progress, cancellation.Token);
            ImportStatusText = result.FolderUnavailable
                ? "Folder Unavailable · Original videos and library entries were preserved."
                : $"Complete · {result.Imported:N0} Added, " +
                  $"{result.AlreadyIndexed:N0} Already Indexed, {result.Failed:N0} Unavailable" +
                  (result.ScanLimitReached ? " · 10,000-File Safety Limit Reached; Use Smaller Folders" : "");
            return result;
        }
        catch (OperationCanceledException)
        {
            ImportStatusText = "Scan Cancelled · Already Indexed Clips Were Kept";
            throw;
        }
        finally
        {
            _importCancellation = null;
            IsImporting = false;
            ReloadClips();
            RefreshConnectedFolders();
        }
    }

    public void CancelImport() => _importCancellation?.Cancel();

    public bool RemoveConnectedFolder(string folderId)
    {
        if (IsImporting) throw new InvalidOperationException("Cancel the active scan first.");
        var removed = _storage.DisconnectFolder(folderId);
        if (removed)
        {
            ReloadClips();
            RefreshConnectedFolders();
            ImportStatusText = "Folder Unlinked · Source Videos Were Not Deleted";
        }
        return removed;
    }

    public async Task RescanLibraryAsync()
    {
        if (CaptureActive || IsExporting)
        {
            StatusDetails = "Finish replay or export before a full library rescan.";
            return;
        }
        if (IsImporting)
        {
            StatusDetails = "A library scan is already running.";
            return;
        }

        try
        {
            await _storage.RescanMediaFolderAsync(_settings.OriginalsFolder);
            foreach (var folder in _storage.GetConnectedFolders())
            {
                if (Directory.Exists(folder.Path))
                    await RescanConnectedFolderAsync(folder.Id);
            }
            StatusDetails = "Library refreshed. Linked videos stay in their original folders.";
        }
        catch (OperationCanceledException)
        {
            StatusDetails = "Library rescan paused to prioritize gameplay.";
        }
        catch (Exception ex)
        {
            StatusDetails = "Couldn't refresh the library: " + ex.Message;
        }
        finally { ReloadClips(); }
    }

    public void OpenOriginalFolder()
    {
        string path = SelectedClip?.OriginalFilePath ?? _settings.OriginalsFolder;
        if (SelectedClip != null && !File.Exists(path))
        {
            StatusDetails = "Original Video Offline · Reconnect Its Linked Folder";
            return;
        }
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            { UseShellExecute = true });
        else if (Directory.Exists(_settings.OriginalsFolder))
            Process.Start(new ProcessStartInfo(_settings.OriginalsFolder)
            { UseShellExecute = true });
    }

    public void SaveClipEdits()
    {
        if (SelectedClip == null) return;
        InvalidateExportReady();
        _storage.AddOrUpdateClip(SelectedClip);
    }

    public bool ApplyTrim(double start, double end)
    {
        if (SelectedClip is null || !SelectedClip.IsAvailable || IsExporting) return false;
        var validated = TrimValidator.Validate(start, end, SelectedClip.DurationSeconds);
        if (!validated.IsValid)
        {
            StatusDetails = validated.ErrorMessage;
            return false;
        }
        if (Math.Abs(SelectedClip.TrimStartSeconds - validated.ValidatedStart) < 1e-6 &&
            Math.Abs(SelectedClip.TrimEndSeconds - validated.ValidatedEnd) < 1e-6) return true;
        var oldStart = SelectedClip.TrimStartSeconds;
        var oldEnd = SelectedClip.TrimEndSeconds;
        SelectedClip.TrimStartSeconds = validated.ValidatedStart;
        SelectedClip.TrimEndSeconds = validated.ValidatedEnd;
        try
        {
            SaveClipEdits();
            StatusDetails = "Trim Saved · The Original Video Is Unchanged";
            return true;
        }
        catch (Exception ex)
        {
            SelectedClip.TrimStartSeconds = oldStart;
            SelectedClip.TrimEndSeconds = oldEnd;
            StatusDetails = "Couldn't Save Trim: " + ex.Message;
            return false;
        }
    }

    private void UnsubscribeAudioEvents()
    {
        foreach (var track in _subscribedAudioTracks)
            track.PropertyChanged -= AudioTrackPropertyChanged;
        _subscribedAudioTracks.Clear();
    }

    private void SubscribeAudioEvents()
    {
        if (_selectedClip is null) return;
        foreach (var track in _selectedClip.AudioTracks)
        {
            track.PropertyChanged += AudioTrackPropertyChanged;
            _subscribedAudioTracks.Add(track);
        }
    }

    private void AudioTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioTrackInfo.Gain) or nameof(AudioTrackInfo.IsMuted))
            ScheduleAudioEdit(); // Only a real changed model value invalidates Ready.
    }

    public void ScheduleAudioEdit()
    {
        if (SelectedClip is null || !SelectedClip.IsAvailable) return;
        InvalidateExportReady(); // Never let stale exported audio look ready.
        _pendingAudioClip = SelectedClip;
        _audioEditCommitTimer.Stop();
        _audioEditCommitTimer.Start();
    }

    public void FlushPendingAudioEdits()
    {
        _audioEditCommitTimer?.Stop();
        var clip = _pendingAudioClip;
        _pendingAudioClip = null;
        if (clip is null) return;
        try { _storage.AddOrUpdateClip(clip); }
        catch (Exception ex) { StatusDetails = "Couldn't Save Audio Mix: " + ex.Message; }
    }

    public void ResetAudioMix()
    {
        if (SelectedClip == null || IsExporting || !SelectedClip.IsAvailable) return;
        foreach (var track in SelectedClip.AudioTracks)
        {
            track.IsMuted = AudioMixDefaults.ShouldMute(
                SelectedClip.IsImported, SelectedClip.AudioTracks.Count, track.TrackIndex);
            track.Gain = 1.0;
        }
        ScheduleAudioEdit();
    }

    public void ReloadClips()
    {
        string? selectedId = SelectedClip?.Id;
        _allClips = _storage.GetAllClips();
        ApplyFilter();
        if (selectedId != null)
        {
            var previous = _allClips.FirstOrDefault(c => c.Id == selectedId);
            if (previous == null)
                SelectedClip = null; // Never keep editing a removed/unlinked entry.
            else if (!ReferenceEquals(SelectedClip, previous))
                SelectedClip = previous;
        }
        UpdateLibrarySummary();
    }

    public void ApplyFilter()
    {
        var query = _searchQuery.Trim();
        var visible = new System.Collections.Generic.List<ClipMetadata>();
        foreach (var clip in _allClips)
        {
            if (_clipSourceFilter == "Flick" && clip.IsImported) continue;
            if (_clipSourceFilter == "Imported" && !clip.IsImported) continue;
            if (query.Length != 0 &&
                !clip.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !clip.TargetName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !clip.LibrarySource.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            visible.Add(clip);
        }
        _filteredClips.ReplaceAll(visible);
    }

    private void OnSelectedClipChanged()
    {
        if (SelectedClip == null)
        {
            InvalidateExportReady();
            return;
        }
        if (!SelectedClip.IsAvailable)
        {
            InvalidateExportReady();
            ExportStatusMessage = "Original Video Offline · Reconnect Its Folder";
            return;
        }

        if (SelectedClip.IsAvailable && !CaptureActive &&
            (string.IsNullOrWhiteSpace(SelectedClip.ThumbnailPath) ||
             !File.Exists(SelectedClip.ThumbnailPath)))
            _ = EnsureSelectedThumbnailAsync(SelectedClip);

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

    private async Task EnsureSelectedThumbnailAsync(ClipMetadata clip)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _thumbnailCancellation, cancellation);
        previous?.Cancel();
        bool acquired = false;
        try
        {
            await _thumbnailGate.WaitAsync(cancellation.Token);
            acquired = true;
            if (CaptureActive || SelectedClip?.Id != clip.Id) return;
            await _storage.EnsureThumbnailAsync(clip.Id, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Keep clip playable even without a thumbnail. */ }
        finally
        {
            if (acquired) _thumbnailGate.Release();
            if (ReferenceEquals(_thumbnailCancellation, cancellation))
                _thumbnailCancellation = null;
            cancellation.Dispose();
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
        if (!CanStartExport || SelectedClip is null) return;
        FlushPendingAudioEdits();
        _importCancellation?.Cancel();
        _thumbnailCancellation?.Cancel();

        var clip = SelectedClip;
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(_settings)) ??
            throw new InvalidOperationException("Couldn't snapshot export settings.");
        snapshot.DiscordTargetSizeMb = TargetSizeMb;
        var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation;
        int generation = ++_exportGeneration;
        InvalidateExportReady();
        IsExporting = true;
        ExportProgress = 0;
        ExportStatusMessage = "Preparing Local MP4…";
        var progress = new Progress<ExportProgress>(item =>
        {
            if (IsExporting && generation == _exportGeneration)
            {
                ExportProgress = Math.Clamp(item.Percent, 0, 99);
                ExportStatusMessage = item.Stage;
            }
        });

        try
        {
            ExportResult result = await _exporter.ExportForDiscordAsync(
                clip, snapshot, progress, cancellation.Token);
            if (result.Success)
            {
                clip.ExportedFilePath = result.ExportFilePath;
                clip.ExportedFileSizeBytes = result.FileSizeBytes;
                clip.LastExportFingerprint = result.Fingerprint;
                clip.IsExportReady = true;
                _storage.AddOrUpdateClip(clip);
                double mb = result.FileSizeBytes / 1_000_000d;
                if (SelectedClip?.Id == clip.Id &&
                    _targetSizeMb == snapshot.DiscordTargetSizeMb)
                {
                    ReadyExportFilePath = result.ExportFilePath;
                    IsExportReady = ReadyShareFile.TryGet(clip, TargetSizeMb, out _);
                    ExportProgress = 100;
                    ExportStatusMessage = IsExportReady
                        ? $"Ready to Drag Into Discord · {mb:F2} MB"
                        : "Export Created But Readiness Verification Failed";
                }
                else
                {
                    ExportStatusMessage = $"Exported {clip.Title} · Select It to Share";
                    OnSelectedClipChanged();
                }
            }
            else
            {
                ExportStatusMessage = "Export Failed: " + result.ErrorMessage;
                StatusDetails = ExportStatusMessage;
            }
        }
        catch (OperationCanceledException)
        {
            ExportStatusMessage = "Export Cancelled · Original Clip Unchanged";
            ExportProgress = 0;
        }
        catch (Exception ex)
        {
            ExportStatusMessage = "Export Failed: " + ex.Message;
            StatusDetails = ExportStatusMessage;
        }
        finally
        {
            if (ReferenceEquals(_exportCancellation, cancellation))
                _exportCancellation = null;
            cancellation.Dispose();
            IsExporting = false;
        }
    }

    public void CopyExportedFileToClipboard()
    {
        if (!TryGetReadyShareFile(out var path))
        {
            ExportStatusMessage = "Export the selected clip before copying.";
            return;
        }
        try
        {
            var files = new System.Collections.Specialized.StringCollection { path };
            Clipboard.SetFileDropList(files);
            ExportStatusMessage = "MP4 File Copied · Paste It Into Discord";
        }
        catch (Exception ex) { ExportStatusMessage = "Couldn't Copy MP4: " + ex.Message; }
    }

    public void OpenExportFolder()
    {
        string? directory = TryGetReadyShareFile(out var path)
            ? Path.GetDirectoryName(path) : _settings.ExportsFolder;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            StatusDetails = "Export Folder Isn't Available · Check Storage Settings";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusDetails = "Couldn't Open Export Folder: " + ex.Message; }
    }

    public void DeleteSelectedClip()
    {
        if (SelectedClip == null) return;
        bool imported = SelectedClip.IsImported;
        string question = imported
            ? $"Remove '{SelectedClip.Title}' from Flick? The original video and any export will NOT be deleted. It will stay excluded from rescans until you unlink and reconnect its folder."
            : $"Delete '{SelectedClip.Title}'? The original video will be sent to the Recycle Bin.";
        var answer = MessageBox.Show(question,
            imported ? "Remove From Library" : "Delete Original Video",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        string id = SelectedClip.Id;
        try
        {
            if (!_storage.DeleteClip(id))
            {
                StatusDetails = "Couldn't remove this clip. The original was preserved.";
                return;
            }
            SelectedClip = null;
            ReloadClips();
            SelectedClip = FilteredClips.FirstOrDefault();
            StatusDetails = imported ? "Imported clip removed from library. Source file kept." :
                "Original moved to the Recycle Bin.";
        }
        catch (Exception ex)
        {
            StatusDetails = "Couldn't change the library: " + ex.Message;
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
        if (!_settings.EnableDiscordWebhook)
        {
            StatusDetails = "Direct Posting Is Disabled · Enable It in Sharing Settings";
            return;
        }
        if (!TryGetReadyShareFile(out var safeFile))
        {
            StatusDetails = "Export a valid MP4 for this clip before sending.";
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
        var res = await client.PostClipAsync(secretUrl, safeFile, message);

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
        _exportCancellation?.Cancel();
        FlushPendingAudioEdits();
        UnsubscribeAudioEvents();
        _importCancellation?.Cancel();
        _thumbnailCancellation?.Cancel();
        _pollTimer.Stop();
        _captureHost.Dispose();
        GC.SuppressFinalize(this);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
