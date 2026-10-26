using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
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
    public string MonitorDevice { get; set; } = "";
    public ulong WindowHwnd { get; set; }
    public uint WindowPid { get; set; }
    public string ExecutablePath { get; set; } = "";
    public ulong ProcessCreationFileTime { get; set; }
    public string DisplayName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Exe { get; set; } = "";

    public override string ToString() => DisplayName;
}

public partial class MainViewModel : INotifyPropertyChanged, IDisposable
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

    // Diagnostics initialize only for an explicitly opened app session, never
    // from an unshown UI fixture. Journals accept closed enums/numbers only.
    private DiagnosticJournal? _diagnosticJournal;
    private Task? _diagnosticInitialization;
    private DiagnosticObservation? _lastDiagnosticObservation;
    private bool _isExportingDiagnostics;
    private CancellationTokenSource? _diagnosticExportCancellation;
    private TaskCompletionSource? _diagnosticExportCompletion;
    private bool _diagnosticsDisposed;
    private string _diagnosticStatusMessage = "Local diagnostics start when Flick opens.";
    public bool DiagnosticsInitialized => _diagnosticJournal != null;
    public bool CanExportDiagnostics => !_isExportingDiagnostics && !_diagnosticsDisposed;
    public string DiagnosticStatusText => _diagnosticStatusMessage;

    private bool _isRecordingDiagnosticRunning;
    private bool _recordingDiagnosticCancelling;
    private bool _isPreparingToQuit;
    private bool _refreshingTargets;
    private bool _diagnosticCleanupUnconfirmed;
    private string _diagnosticTestStatus = "Test recording and saving after confirmation.";
    private string _diagnosticTestDetails = "";
    private CancellationTokenSource? _recordingDiagnosticCancellation;
    private Task? _recordingDiagnosticCancellationDispatch;
    private TaskCompletionSource? _recordingDiagnosticCompletion;
    public bool IsRecordingDiagnosticRunning => _isRecordingDiagnosticRunning;
    public bool DiagnosticHelperNeedsRestart => _diagnosticCleanupUnconfirmed;
    public bool CanChangeCaptureSource => !_isRecordingDiagnosticRunning && !_isPreparingToQuit && !_diagnosticCleanupUnconfirmed
        && !_filmstripCleanupUnconfirmed && !_thumbnailCleanupUnconfirmed && !_thumbnailStopPending && !_audioPreviewOperation.CleanupFailed && !_gameAutomationBusy;
    public bool CanControlReplay => CanChangeCaptureSource && !_refreshingTargets && _manualStopOperation == null;

    public Func<bool>? DiagnosticReservationAllowed { get; set; }
    public bool CanRunRecordingDiagnostic => !_diagnosticsDisposed && !_isPreparingToQuit &&
        !_isRecordingDiagnosticRunning && !_diagnosticCleanupUnconfirmed && !_filmstripCleanupUnconfirmed && !_thumbnailCleanupUnconfirmed && !_thumbnailStopPending &&
        !_isBuildingAudioPreview && !_audioPreviewOperation.CleanupFailed && !_refreshingTargets && !CaptureActive && _stopCaptureTask == null &&
        !IsReplaySavePending && !IsImporting && !IsExporting && !_isExportingDiagnostics &&
        Volatile.Read(ref _discordUploadOperations) == 0 && Volatile.Read(ref _libraryRescanOperations) == 0 &&
        !_pollInProgress && !_gameAutomationBusy && SelectedTarget != null && Targets.Contains(SelectedTarget) &&
        DiagnosticReservationAllowed?.Invoke() != false;
    public bool CanInvokeRecordingDiagnostic => _isRecordingDiagnosticRunning
        ? !_recordingDiagnosticCancelling : CanRunRecordingDiagnostic;
    public string DiagnosticTestActionText => _isRecordingDiagnosticRunning
        ? (_recordingDiagnosticCancelling ? "Finishing test…" : "Cancel test") : "Run recording test";
    public string DiagnosticTestStatusText => _diagnosticTestStatus;
    public string DiagnosticTestDetailsText => _diagnosticTestDetails;

    private void PublishDiagnosticTest()
    {
        OnPropertyChanged(nameof(IsRecordingDiagnosticRunning));
        OnPropertyChanged(nameof(DiagnosticHelperNeedsRestart));
        OnPropertyChanged(nameof(CanRunRecordingDiagnostic));
        OnPropertyChanged(nameof(CanInvokeRecordingDiagnostic));
        OnPropertyChanged(nameof(CanChangeCaptureSource));
        OnPropertyChanged(nameof(CanControlReplay));
        OnPropertyChanged(nameof(DiagnosticTestActionText));
        OnPropertyChanged(nameof(DiagnosticTestStatusText));
        OnPropertyChanged(nameof(DiagnosticTestDetailsText));
        OnPropertyChanged(nameof(IsCaptureActive));
        OnPropertyChanged(nameof(IsBackgroundWorkActive));
        OnPropertyChanged(nameof(CanStartExport));
    }

    public RecordingDiagnosticPlan? CreateRecordingDiagnosticPlan(bool includeConfiguredAudio)
    {
        if (!CanRunRecordingDiagnostic || SelectedTarget is not { } target)
        {
            _diagnosticTestStatus = "Finish replay or background work and select a source before testing.";
            PublishDiagnosticTest();
            return null;
        }
        try
        {
            return RecordingDiagnosticPlan.Create(_settings,
                new RecordingDiagnosticTarget(target.Type, target.DisplayName,
                    target.MonitorIndex, target.MonitorDevice, target.WindowHwnd,
                    target.WindowPid, target.Title, target.Exe, target.ExecutablePath, target.ProcessCreationFileTime),
                enableSystemAudio: includeConfiguredAudio,
                enableMicrophone: includeConfiguredAudio && _settings.EnableMicrophone);
        }
        catch (ArgumentException)
        {
            _diagnosticTestStatus = "Refresh and select the source again, then review the recording profile.";
            PublishDiagnosticTest();
            return null;
        }
    }

    public void CancelRecordingDiagnostic()
    {
        if (!_isRecordingDiagnosticRunning || _recordingDiagnosticCancelling) return;
        _recordingDiagnosticCancelling = true;
        _diagnosticTestStatus = "Cancelling test and releasing its recording helper…";
        if (_recordingDiagnosticCancellation is { } cancellation)
            _recordingDiagnosticCancellationDispatch = DispatchDiagnosticCancellationAsync(cancellation);
        PublishDiagnosticTest();
    }

    private static async Task DispatchDiagnosticCancellationAsync(CancellationTokenSource cancellation)
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception) { /* The runner still owns and verifies helper cleanup. */ }
    }

    public async Task<bool> CancelRecordingDiagnosticAndWaitAsync()
    {
        CancelRecordingDiagnostic();
        if (_recordingDiagnosticCompletion is { } test)
        {
            try { await test.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
            catch (TimeoutException) { return false; }
        }
        return !_isRecordingDiagnosticRunning && !_diagnosticCleanupUnconfirmed;
    }

    public async Task RunRecordingDiagnosticAsync(RecordingDiagnosticPlan plan, Guid confirmedPlanId)
    {
        // Recheck after the consent dialog's nested dispatcher. A hotkey, quit
        // or another operation may have run while that dialog was visible.
        if (!CanRunRecordingDiagnostic || plan.PlanId != confirmedPlanId || SelectedTarget is not { } selected ||
            plan.Target.Kind != selected.Type || plan.Target.WindowHwnd != selected.WindowHwnd ||
            plan.Target.WindowPid != selected.WindowPid || plan.Target.MonitorId != selected.MonitorDevice ||
            plan.Target.WindowTitle != selected.Title || plan.Target.WindowExecutable != selected.Exe ||
            plan.Target.WindowExecutablePath != selected.ExecutablePath || plan.Target.WindowProcessCreationFileTime != selected.ProcessCreationFileTime)
        {
            _diagnosticTestStatus = "The source or app state changed. Review the test again before recording.";
            PublishDiagnosticTest();
            return;
        }
        _isRecordingDiagnosticRunning = true; // Reserve before the first await.
        ClearFilmstrip();
        _recordingDiagnosticCancelling = false;
        _diagnosticTestStatus = "Starting the confirmed recording test…";
        _diagnosticTestDetails = "Audio is captured only as described in the confirmed test.";
        _thumbnailCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _recordingDiagnosticCancellation = cancellation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _recordingDiagnosticCompletion = completion;
        PublishDiagnosticTest();
        try
        {
            if (!await StopSelectedThumbnailAsync()) throw new InvalidOperationException("Thumbnail work is still stopping. Wait before testing recording.");
            if (!await StopFilmstripAsync()) throw new InvalidOperationException("Preview frames are still stopping. Wait before testing recording.");
            var runner = new RecordingDiagnosticRunner(_ => new NativeRecordingDiagnosticLease(),
                async (frozen, replay, token) => await _storage.RegisterNewOriginalAsync(
                    replay.FilePath, frozen.Source.Name, replay.TrackNames, token).ConfigureAwait(false));
            var progress = new Progress<RecordingDiagnosticProgress>(p => {
                if (_recordingDiagnosticCancelling || !_isRecordingDiagnosticRunning ||
                    !ReferenceEquals(_recordingDiagnosticCompletion, completion)) return;
                _diagnosticTestStatus = p.Stage switch {
                    RecordingDiagnosticStage.Starting => "Starting the test helper…",
                    RecordingDiagnosticStage.WaitingForReady => "Waiting for a decodable replay buffer…",
                    RecordingDiagnosticStage.Saving => "Saving the test clip…",
                    RecordingDiagnosticStage.Validating => "Validating and indexing the test clip…",
                    RecordingDiagnosticStage.Stopping => "Releasing the test helper…",
                    _ => "Finishing recording test…"
                };
                OnPropertyChanged(nameof(DiagnosticTestStatusText));
            });
            var result = await Task.Run(() => runner.RunAsync(plan, confirmedPlanId, progress, cancellation.Token));
            _diagnosticCleanupUnconfirmed = !result.HelperExitConfirmed;
            _diagnosticTestStatus = _diagnosticCleanupUnconfirmed
                ? "Helper exit was not confirmed. Quit and reopen Flick before recording."
                : result.Succeeded
                ? $"Test passed · {result.ValidatedClip!.DurationSeconds:F1}s clip kept in Your Clips."
                : result.Outcome == RecordingDiagnosticOutcome.Cancelled ? "Test cancelled. Any recorded original was kept."
                : "Test did not pass. See the details above.";
            string Phase(string label, TimeSpan? elapsed) => elapsed is { } duration ? $"{label}: {duration.TotalSeconds:F2}s" : $"{label}: not completed";
            _diagnosticTestDetails = $"Last recording test: {result.LocalMessage}\n{plan.AudioSummary}\n" +
                $"Actual profile: {result.ActualProfile?.DisplayText ?? "Not negotiated"}\n" +
                string.Join(" · ", Phase("Start", result.Timings.Startup), Phase("Ready", result.Timings.Readiness),
                    Phase("Save", result.Timings.Save), Phase("Validation", result.Timings.Validation), Phase("Cleanup", result.Timings.Cleanup)) +
                $"\nTest helper exited: {(result.HelperExitConfirmed ? "Confirmed" : "Not confirmed")}";
            _diagnosticJournal?.TryRecord(new DiagnosticObservation(DiagnosticEventKind.DiagnosticTestCompleted,
                SourceKind: plan.Target.Kind == "Window" ? DiagnosticSourceKind.Window : DiagnosticSourceKind.Monitor,
                Encoder: DiagnosticPolicy.KnownEncoder(result.ActualProfile?.EncoderId),
                Error: result.Succeeded ? DiagnosticErrorCode.None : DiagnosticErrorCode.Unknown,
                Width: result.ActualProfile?.Width, Height: result.ActualProfile?.Height, Fps: result.ActualProfile?.Fps,
                DurationMilliseconds: result.ValidatedClip is { } validated ? (long)(validated.DurationSeconds * 1000) : null,
                LatencyMilliseconds: result.Timings.Total is { } total ? (long)total.TotalMilliseconds : null));
            if (result.ValidatedClip != null)
            {
                var editing = SelectedClip;
                ReloadClips();
                SelectedClip = editing == null ? result.ValidatedClip
                    : FilteredClips.FirstOrDefault(c => c.Id == editing.Id) ?? editing;
            }
        }
        catch (Exception)
        {
            _diagnosticTestStatus = "The test could not finish. Your existing clips and preferences were kept.";
            _diagnosticTestDetails = "Review the selected source, recording profile and storage access before trying again.";
        }
        finally
        {
            if (_recordingDiagnosticCancellationDispatch is { } dispatched)
                await dispatched;
            _isRecordingDiagnosticRunning = false;
            _recordingDiagnosticCancelling = false;
            _recordingDiagnosticCancellation = null;
            _recordingDiagnosticCancellationDispatch = null;
            _recordingDiagnosticCompletion = null;
            completion.TrySetResult();
            PublishDiagnosticTest();
        }
    }

    private Task InitializeDiagnosticsAsync() => _diagnosticInitialization ??= InitializeDiagnosticsCoreAsync();

    private async Task InitializeDiagnosticsCoreAsync()
    {
        if (_diagnosticsDisposed) return;
        try
        {
            var assembly = typeof(MainViewModel).Assembly;
            string? versionText = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!DiagnosticAppVersion.TryParse(versionText, out var version))
            {
                var binaryVersion = assembly.GetName().Version ?? new Version(0, 0, 0);
                version = new DiagnosticAppVersion(binaryVersion.Major, binaryVersion.Minor,
                    Math.Max(0, binaryVersion.Build), DiagnosticReleaseChannel.Preview);
            }
            string directory = Path.Combine(Path.GetDirectoryName(AppSettings.GetSettingsFilePath())!, "Diagnostics");
            // Even metadata queries can block on unavailable storage. Keep the
            // entire filesystem operation off the WPF dispatcher.
            var journal = await Task.Run(() => DiagnosticJournal.OpenAsync(directory, version));
            if (_diagnosticsDisposed) { await journal.DisposeAsync(); return; }
            _diagnosticJournal = journal;
            OnPropertyChanged(nameof(DiagnosticsInitialized));
            RecordDiagnostic(DiagnosticEventKind.ApplicationStarted);
            // Window shortcuts register before asynchronous app initialization.
            // Project their outcomes now; never persist their chord or message.
            foreach (var hotkey in _health.Snapshot.Hotkeys)
                RecordDiagnosticHotkey(hotkey.Action, hotkey.IsRegistered, hotkey.Message);
            UpdateDiagnosticStatus();
        }
        catch (Exception)
        {
            // Logging must never prevent replay. Never copy exception paths or
            // raw settings into a troubleshooting report or status DTO.
            _diagnosticStatusMessage = "Local diagnostics could not start. Recording remains available.";
            OnPropertyChanged(nameof(DiagnosticStatusText));
        }
    }

    private void UpdateDiagnosticStatus()
    {
        if (_diagnosticJournal == null) return;
        _diagnosticStatusMessage = _diagnosticJournal.Status.StorageState switch {
            DiagnosticStorageState.Ready => "Local diagnostics · up to 256 events, kept for 7 days.",
            DiagnosticStorageState.Malformed => "An unreadable local log was preserved. You can export this session.",
            DiagnosticStorageState.Disposed => "Local diagnostic logging is closed.",
            _ => "Local log storage is unavailable. You can still export this session."
        };
        OnPropertyChanged(nameof(DiagnosticStatusText));
    }

    public async Task RefreshDiagnosticsStatusAsync()
    {
        if (_diagnosticJournal == null || _diagnosticsDisposed) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await _diagnosticJournal.FlushAsync(timeout.Token); }
        catch (OperationCanceledException) { }
        UpdateDiagnosticStatus();
    }

    private void RecordDiagnostic(DiagnosticEventKind kind, DiagnosticErrorCode error = DiagnosticErrorCode.None)
    {
        var journal = _diagnosticJournal;
        if (journal == null || _diagnosticsDisposed) return;
        var observation = DiagnosticObservation.FromHealth(kind, _health.Snapshot, error);
        // A repeated failed poll must not turn into one persistent write per tick.
        if (observation == _lastDiagnosticObservation) return;
        if (journal.TryRecord(observation)) _lastDiagnosticObservation = observation;
    }

    // Correlate an action with recording state without logging any raw keystroke,
    // window title, executable path or identifying process details.
    private void RecordReplayControl(DiagnosticControlOrigin origin, DiagnosticControlAction action)
    {
        if (origin == DiagnosticControlOrigin.Unknown || _diagnosticsDisposed) return;
        _diagnosticJournal?.TryRecord(DiagnosticObservation.FromHealth(
            DiagnosticEventKind.ReplayControl, _health.Snapshot) with
        {
            ControlOrigin = origin,
            ControlAction = action
        });
    }

    private static DiagnosticErrorCode DiagnosticFailure(string code, string message = "")
    {
        if (message.Contains("runtime", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("executable not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("DLL", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ffprobe", StringComparison.OrdinalIgnoreCase)) return DiagnosticErrorCode.DependencyMissing;
        if (message.Contains("encoder", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("GPU driver", StringComparison.OrdinalIgnoreCase)) return DiagnosticErrorCode.EncoderUnavailable;
        return code switch {
            "missing-source" => DiagnosticErrorCode.MissingSource,
            "startup-refused" => DiagnosticErrorCode.StartupRefused,
            "startup-failed" => DiagnosticErrorCode.StartupFailed,
            "host-unresponsive" => DiagnosticErrorCode.HostUnresponsive,
            "host-exited" => DiagnosticErrorCode.HostExited,
            "unexpected-stop" or "capture-fault" or "target-or-engine-fault" => DiagnosticErrorCode.EngineFault,
            "forced-stop" or "stop-failed" => DiagnosticErrorCode.ForcedStop,
            "not-ready" => DiagnosticErrorCode.NotReady,
            _ => DiagnosticErrorCode.Unknown
        };
    }

    public async Task ExportDiagnosticsAsync(string destination)
    {
        if (!CanExportDiagnostics) return;
        _isExportingDiagnostics = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _diagnosticExportCompletion = completion;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _diagnosticExportCancellation = timeout;
        OnPropertyChanged(nameof(CanExportDiagnostics));
        OnPropertyChanged(nameof(IsBackgroundWorkActive));
        try
        {
            if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                _diagnosticStatusMessage = "Choose a ZIP filename for the troubleshooting report.";
                return;
            }
            await InitializeDiagnosticsAsync().WaitAsync(timeout.Token);
            if (_diagnosticJournal == null)
            {
                _diagnosticStatusMessage = "Diagnostics are unavailable. Recording remains available.";
                return;
            }
            var journal = _diagnosticJournal;
            var result = await Task.Run(() => journal.ExportZipAsync(destination,
                overwriteConfirmed: true, cancellationToken: timeout.Token), timeout.Token);
            _diagnosticStatusMessage = result.Success
                ? "Diagnostics exported locally. No videos or personal file details included."
                : "Could not export diagnostics. Choose an accessible folder and try again.";
        }
        catch (OperationCanceledException) { _diagnosticStatusMessage = "Diagnostic export cancelled. The existing report was kept."; }
        catch (Exception) { _diagnosticStatusMessage = "Could not export diagnostics. Choose an accessible folder and try again."; }
        finally
        {
            _isExportingDiagnostics = false;
            _diagnosticExportCancellation = null;
            _diagnosticExportCompletion = null;
            completion.TrySetResult();
            OnPropertyChanged(nameof(DiagnosticStatusText));
            OnPropertyChanged(nameof(CanExportDiagnostics));
            OnPropertyChanged(nameof(IsBackgroundWorkActive));
        }
    }

    // Engine health and save completion are independent: a stopped engine may
    // still have a real save being validated. Source identity is fixed at request.
    private readonly RecordingHealthLedger _health = new();
    private Guid _healthSession;
    private int _captureEpoch;
    private bool _pollInProgress;
    private readonly System.Collections.Generic.Dictionary<string, (Guid Session, string Target, bool Validating)> _saveContexts = new(StringComparer.Ordinal);

    public RecordingHealthSnapshot RecordingHealth => _health.Snapshot;
    public string HealthStatusText => _health.Snapshot.StatusText;
    public string HealthDetailsText
    {
        get
        {
            var h = _health.Snapshot;
            string source = h.Source?.Name ?? SelectedTarget?.DisplayName ?? "No source selected";
            string profile = h.Profile?.DisplayText ?? "Not recording";
            string previous = h.LastProfile is null || h.Profile != null ? "" : $"\nLast recording: {h.LastProfile.DisplayText}";
            string buffer = h.EngineState is RecordingEngineState.Ready or RecordingEngineState.Buffering
                ? $"{(h.BufferedDurationEstimated ? "~" : "")}{h.BufferedSeconds:F1}s / {h.MaximumBufferSeconds:F0}s{(h.BufferTrackingLimited ? " · limited tracking" : "")}" : "No active replay buffer";
            return $"Engine: {h.EngineState} · Save: {h.SaveState}\nSource: {source}\nEncoder / profile: {profile}{previous}\nBuffer: {buffer}\nHost: {(_healthSession == Guid.Empty ? (_captureHost.IsRunning ? "Helper available · Replay Off" : "Stopped") : h.HostHealth.ToString())}";
        }
    }
    public string HealthLastFailureText
    {
        get
        {
            var h = _health.Snapshot;
            return $"Last recording error: {h.LastRecordingIssue?.Message ?? "None reported"}\nLast save error: {h.LastSaveIssue?.Message ?? "None reported"}";
        }
    }
    public string HealthLastSavedText => _health.Snapshot.LastValidatedClip is { } clip
        ? $"Last validated clip: {clip.DurationSeconds:F1} sec · {clip.SavedAt.ToLocalTime():g}"
        : "No clip validated in this session";
    public string HealthHotkeysText => _health.Snapshot.Hotkeys.Count == 0
        ? "Shortcuts register when the window opens."
        : string.Join("\n", _health.Snapshot.Hotkeys.Select(h =>
            $"{h.Action}: {(h.IsRegistered ? h.RegisteredChord : "Unavailable")} · {h.Message}"));

    public void ReportHotkeyHealth(string action, string requested, string? registered, string message = "")
    {
        _health.SetHotkeyHealth(new RecordingHotkeyHealth(action, requested, registered, registered != null, Message: message));
        RecordDiagnosticHotkey(action, registered != null, message);
        PublishHealth();
    }

    private void RecordDiagnosticHotkey(string action, bool registered, string message)
    {
        if (_diagnosticsDisposed) return;
        _diagnosticJournal?.TryRecord(new DiagnosticObservation(DiagnosticEventKind.HotkeyChanged,
            HotkeyAction: action == "Save replay" ? DiagnosticHotkeyAction.SaveReplay : DiagnosticHotkeyAction.ToggleReplay,
            HotkeyRegistered: registered, Error: !registered || !string.IsNullOrWhiteSpace(message)
                ? DiagnosticErrorCode.HotkeyConflict : DiagnosticErrorCode.None));
    }

    private void PublishHealth()
    {
        OnPropertyChanged(nameof(RecordingHealth));
        OnPropertyChanged(nameof(HealthStatusText));
        OnPropertyChanged(nameof(HealthDetailsText));
        OnPropertyChanged(nameof(HealthLastFailureText));
        OnPropertyChanged(nameof(HealthLastSavedText));
        OnPropertyChanged(nameof(HealthHotkeysText));
        OnPropertyChanged(nameof(CanRunRecordingDiagnostic));
        OnPropertyChanged(nameof(CanInvokeRecordingDiagnostic));
    }

    private void RecordCaptureFailure(string code, string message, bool faulted = true)
    {
        _health.RecordRecordingIssue(_healthSession, code, message, faulted);
        RecordDiagnostic(DiagnosticEventKind.RecordingFailed, DiagnosticFailure(code, message));
        StatusDetails = message;
        PublishHealth();
    }

    private void RecordSaveFailure(string saveId, string reason, DiagnosticErrorCode error = DiagnosticErrorCode.Unknown)
    {
        if (_saveContexts.Remove(saveId, out var context))
            _health.RecordSaveFailure(context.Session, saveId, "save-failed", reason);
        else _health.RecordSaveIssue("save-failed", reason);
        RecordDiagnostic(DiagnosticEventKind.SaveFailed, error);
        StatusDetails = reason;
        PublishHealth();
    }

    // Capture state
    private string _captureStatus = "Off";
    private string _bufferedDurationText = "0.0s / 30.0s";
    private bool _isArmed = false;
    private string _statusDetails = "";
    private string _resourceStatsText = "RAM -- · CPU --";
    private CaptureEncodingProfile? _captureProfile;

    // Targets
    private ObservableCollection<TargetItem> _targets = new();
    private TargetItem? _selectedTarget;

    // Clips
    private System.Collections.Generic.List<ClipMetadata> _allClips = new();
    private LibraryObservableCollection<ClipMetadata> _filteredClips = new();
    private ClipMetadata? _selectedClip;
    private string _searchQuery = "";
    private string _clipSourceFilter = "All Clips";
    private string _librarySummaryText = "0 clips";
    private string _importStatusText = "";
    private bool _isImporting;
    private CancellationTokenSource? _importCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private readonly SemaphoreSlim _thumbnailGate = new(1, 1);
    private readonly HashSet<Task> _thumbnailOperations = [];
    private bool _thumbnailCleanupUnconfirmed;
    private bool _thumbnailStopPending;
    private bool CanGenerateSelectedThumbnail => !IsCaptureActive && !IsExporting && !IsImporting &&
        !_isLoadingFilmstrip && !_isBuildingAudioPreview && !_isExportingDiagnostics && !_isPreparingToQuit &&
        !_thumbnailCleanupUnconfirmed && !_thumbnailStopPending && !_filmstripCleanupUnconfirmed && !_audioPreviewOperation.CleanupFailed &&
        Volatile.Read(ref _libraryRescanOperations) == 0 && Volatile.Read(ref _discordUploadOperations) == 0;
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
    private int _saveRequestOperations;
    private int _replayIndexOperations;
    private int _discordUploadOperations;
    private int _libraryRescanOperations;
    private Task? _stopCaptureTask;
    public bool IsReplaySavePending => _saveRequestOperations > 0 || _replayIndexOperations > 0
        || _captureHost.HasPendingReplaySaves;
    // These operations outlive their initiating page. The updater also checks
    // the separate capture/save/import/export flags before closing the app.
    public bool IsBackgroundWorkActive => Volatile.Read(ref _discordUploadOperations) > 0
        || Volatile.Read(ref _libraryRescanOperations) > 0 || _isExportingDiagnostics || _isRecordingDiagnosticRunning
        || _isBuildingAudioPreview || _audioPreviewOperation.CleanupFailed || _isLoadingFilmstrip || _filmstripCleanupUnconfirmed || _gameAutomationBusy || _thumbnailOperations.Count > 0 || _thumbnailCleanupUnconfirmed;
    public bool IsCaptureActive => CaptureActive || _gameAutomationBusy || _isRecordingDiagnosticRunning || _diagnosticCleanupUnconfirmed;
    public bool CanEditClip => SelectedClip?.IsAvailable == true && !IsExporting && !_isBuildingAudioPreview;
    public bool CanStartExport => CanEditClip && !IsImporting && !_gameAutomationBusy && !_isRecordingDiagnosticRunning && !_isPreparingToQuit
        && !_diagnosticCleanupUnconfirmed && !_filmstripCleanupUnconfirmed && !_thumbnailCleanupUnconfirmed && !_thumbnailStopPending && !_audioPreviewOperation.CleanupFailed;

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
        LibrarySummaryText = $"{all} {(all == 1 ? "clip" : "clips")} · " +
                             $"{folders} {(folders == 1 ? "folder" : "folders")}";
    }

    public string CaptureStatus
    {
        get => _captureStatus;
        set
        {
            bool engineChanged = _captureStatus != value;
            _captureStatus = value;
            IsArmed = value == "Armed";
            if (value is "Off" or "Faulted" or "Paused")
            {
                SetCaptureProfile(null);
                BufferedDurationText = "No active replay buffer";
            }
            _health.ObserveEngine(_healthSession, value switch {
                "Starting" => RecordingEngineState.Starting,
                "Buffering" => RecordingEngineState.Buffering,
                "Armed" => RecordingEngineState.Ready,
                "Stopping" => RecordingEngineState.Stopping,
                "Faulted" => RecordingEngineState.Faulted,
                _ => RecordingEngineState.Off
            });
            if (engineChanged) RecordDiagnostic(DiagnosticEventKind.EngineChanged);
            OnPropertyChanged();
            PublishHealth();
        }
    }

    public string CaptureEncodingText => _captureProfile?.DisplayText ?? "";
    public string SoftwareFallbackWarning => _captureProfile?.SoftwareWarning ?? "";

    private void SetCaptureProfile(CaptureEncodingProfile? profile)
    {
        if (_captureProfile == profile) return;
        _captureProfile = profile;
        if (profile != null)
        {
            _health.ObserveProfile(_healthSession, profile);
            RecordDiagnostic(DiagnosticEventKind.CaptureConfigured);
        }
        PublishHealth();
        OnPropertyChanged(nameof(CaptureEncodingText));
        OnPropertyChanged(nameof(SoftwareFallbackWarning));
    }

    private void OnCaptureConfigured(CaptureEncodingProfile profile)
    {
        var observedSession = _health.Snapshot.SessionId;
        int observedEpoch = _captureEpoch;
        int? observedHost = _captureHost.HostProcessId;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (observedSession != _healthSession || observedEpoch != _captureEpoch ||
                observedHost != _captureHost.HostProcessId) return;
            if (CaptureStatus is "Starting" or "Buffering" or "Armed") SetCaptureProfile(profile);
        });
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
        set { if (_resourceStatsText == value) return; _resourceStatsText = value; OnPropertyChanged(); }
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
                if (!CanChangeCaptureSource) { OnPropertyChanged(); return; }
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
                PublishDiagnosticTest();
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
                if (value == null && _applyingLibraryFilter) return;
                if (_selectedClip != null && value != null && HasUnsavedOrganization && ConfirmDiscardEditorChanges?.Invoke() != true)
                { OnPropertyChanged(); return; }
                FlushPendingAudioEdits();
                UnsubscribeAudioEvents();
                _selectedClip = value;
                OrganizationSelectionChanged();
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
            int previous = _settings.DiscordTargetSizeMb;
            _settings.DiscordTargetSizeMb = value;
            try { _settings.Save(); }
            catch
            {
                _settings.DiscordTargetSizeMb = previous;
                throw;
            }
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
        bool ready = !IsExporting && IsExportReady &&
               ReadyShareFile.TryGet(SelectedClip, TargetSizeMb, out path, _settings.ExportsFolder) &&
               string.Equals(path, ReadyExportFilePath, StringComparison.OrdinalIgnoreCase);
        if (!ready && !IsExporting && IsExportReady)
        {
            IsExportReady = false;
            ReadyExportFilePath = "";
            ExportStatusMessage = "The share file changed or is missing. Export this clip again.";
        }
        return ready;
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
        InitializeResourceSampling();

        _targetSizeMb = _settings.DiscordTargetSizeMb;

        // Wire IPC events
        _captureHost.StateChanged += OnHostStateChanged;
        _captureHost.ReplaySavedWithTracks += OnHostReplaySaved;
        _captureHost.FaultOccurred += OnHostFaultOccurred;
        _captureHost.CaptureConfigured += OnCaptureConfigured;
        _captureHost.EventReceived += OnHostEventReceived;
        _captureHost.PendingReplaySavesChanged += OnPendingReplaySavesChanged;
        _captureHost.ReplaySaveAborted += OnReplaySaveAborted;

        // Status polling timer
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += OnResourcePollTick;
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
        // Optional log storage must not delay source discovery or replay.
        _ = InitializeDiagnosticsAsync();
        await RefreshTargetsAsync();
        await ApplyGameDetectionPreferencesAsync();
    }

    public async Task RefreshTargetsAsync()
    {
        if (!CanChangeCaptureSource || _refreshingTargets) return;
        _refreshingTargets = true;
        PublishDiagnosticTest();
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
                    MonitorDevice = m.Device,
                    DisplayName = m.DisplayName
                });
            }

            foreach (var w in targets.Windows)
            {
                Targets.Add(new TargetItem
                {
                    Type = "Window",
                    WindowHwnd = w.Hwnd,
                    WindowPid = w.Pid,
                    ExecutablePath = w.ExecutablePath,
                    ProcessCreationFileTime = w.ProcessCreationFileTime,
                    DisplayName = w.DisplayName,
                    Title = w.Title,
                    Exe = w.Exe
                });
            }

            if (Targets.Count > 0 && SelectedTarget == null)
            {
                // Never silently replace a previously authorized game window
                // with desktop/monitor capture. Window handles may be recycled,
                // so require matching title AND executable on a new session.
                var previouslyAuthorized = Targets.FirstOrDefault(target =>
                    CaptureTargetSelectionRules.MatchesSavedPreference(
                        _settings, target.Type, target.MonitorIndex,
                        target.Title, target.Exe));
                if (previouslyAuthorized != null)
                    SelectedTarget = previouslyAuthorized;
                else
                    StatusDetails = "Saved capture source isn't available. Choose a source before starting replay.";
            }
        }
        catch (Exception ex)
        {
            StatusDetails = "Target refresh error: " + ex.Message;
        }
        finally
        {
            // Target discovery must not leave an idle native helper running.
            try
            {
                if (CaptureStatus == "Off") await _captureHost.ShutdownAsync();
            }
            finally { _refreshingTargets = false; PublishDiagnosticTest(); }
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

    public async Task ToggleCaptureAsync(DiagnosticControlOrigin origin = DiagnosticControlOrigin.Unknown)
    {
        if (CaptureStatus is "Starting" or "Stopping") return;
        bool stopping = _automaticGame != null ||
            CaptureStatus is not ("Off" or "Faulted" or "Paused");
        if (stopping && origin == DiagnosticControlOrigin.GlobalHotkey && IsReplaySavePending)
        {
            // A save key event must never incidentally stop the buffer while
            // the last MKV is still being completed or indexed. Explicit UI/tray
            // Stop remains available; it settles the pending save normally.
            RecordReplayControl(origin, DiagnosticControlAction.StopDeferred);
            StatusDetails = "The previous clip is still saving. Replay remains active; use Stop Replay after it finishes.";
            return;
        }
        RecordReplayControl(origin, stopping ? DiagnosticControlAction.Stop : DiagnosticControlAction.Start);
        if (stopping) await StopCaptureAsync();
        else await StartCaptureAsync();
    }

    public async Task StartCaptureAsync()
    {
        if (_manualStopOperation != null) return;
        await QuiesceGameAutomationAsync();
        if (_manualStopOperation != null || _automaticGame != null) return;
        if (_isRecordingDiagnosticRunning || _isPreparingToQuit || _diagnosticCleanupUnconfirmed || _filmstripCleanupUnconfirmed || _thumbnailCleanupUnconfirmed || _audioPreviewOperation.CleanupFailed || _refreshingTargets) return;

        if (CaptureActive || _stopCaptureTask != null) return;
        if (SelectedTarget is null || !Targets.Contains(SelectedTarget))
        {
            // Reject an absent or stale selection before invoking the native
            // recorder, including when triggered by the global hotkey.
            CaptureStatus = "Off";
            RecordCaptureFailure("missing-source", "Select an available window or monitor before starting replay.", false);
            return;
        }
        _manualReplayReserved = true;
        PublishGameAutomation("Manual replay owns recording. Automatic game replay waits until it stops.");
        // Never let folder indexing compete with the live gameplay encoder.
        _importCancellation?.Cancel();
        _thumbnailCancellation?.Cancel();
        ClearFilmstrip();
        _captureEpoch++;
        _healthSession = _health.BeginSession(new RecordingSource(SelectedTarget.Type,
            SelectedTarget.DisplayName, SelectedTarget.Exe));
        SetCaptureProfile(null);
        CaptureStatus = "Starting";
        StatusDetails = "Initializing capture encoder and audio...";
        int startingEpoch = _captureEpoch;
        Guid startingSession = _healthSession;
        try
        {
            if (!await StopSelectedThumbnailAsync()) throw new InvalidOperationException("Thumbnail work is still stopping. Wait before starting replay.");
            if (!await CancelAudioPreviewAndWaitAsync()) throw new InvalidOperationException("Audio preview cleanup is not finished; wait before starting replay.");
            if (!await StopFilmstripAsync()) throw new InvalidOperationException("Preview frames are still stopping. Wait before starting replay.");
            if (startingEpoch != _captureEpoch || startingSession != _healthSession || _isPreparingToQuit || _manualStopOperation != null) return;
            await ReserveManualGameSessionsAsync();
            if (startingEpoch != _captureEpoch || startingSession != _healthSession || _isPreparingToQuit || _manualStopOperation != null) return;
            CaptureStartOptions? sourceOptions = null;
            if (SelectedTarget.Type == "Window")
            {
                if (string.IsNullOrWhiteSpace(SelectedTarget.ExecutablePath) || SelectedTarget.ProcessCreationFileTime == 0 || SelectedTarget.WindowPid == 0)
                    throw new InvalidOperationException("Refresh capture sources and reselect this window before recording.");
                var capabilities = await _captureHost.GetCapabilitiesAsync();
                if (startingEpoch != _captureEpoch || startingSession != _healthSession || _isPreparingToQuit || _manualStopOperation != null) return;
                if (!capabilities.ExactWindowBinding) throw new InvalidOperationException("Update the capture helper before recording this exact window.");
                sourceOptions = new(ExpectedWindow: new(SelectedTarget.WindowHwnd, SelectedTarget.WindowPid,
                    SelectedTarget.Title, SelectedTarget.Exe, SelectedTarget.ExecutablePath, SelectedTarget.ProcessCreationFileTime));
            }
            bool started = await _captureHost.StartCaptureAsync(_settings, sourceOptions);

            if (startingEpoch != _captureEpoch || startingSession != _healthSession) return;
            if (!started)
            {
                CaptureStatus = "Faulted";
                RecordCaptureFailure("startup-refused", _captureHost.LastStartError);
            }
        }
        catch (Exception ex)
        {
            if (startingEpoch != _captureEpoch || startingSession != _healthSession) return;
            CaptureStatus = "Faulted";
            RecordCaptureFailure("startup-failed", "Capture startup failed: " + ex.Message);
        }
    }

    public async Task StopCaptureAsync(DiagnosticControlOrigin origin = DiagnosticControlOrigin.Unknown)
    {
        if (origin != DiagnosticControlOrigin.Unknown)
            RecordReplayControl(origin, DiagnosticControlAction.Stop);
        if (_manualStopOperation is { } stopping) { await stopping; return; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _manualStopOperation = completion.Task;
        PublishGameAutomation();
        try
        {
            if (CaptureStatus == "Starting") await Task.Run(() => _captureHost.Dispose());
            await StopRequestedCaptureAsync();
        }
        finally { _manualStopOperation = null; completion.TrySetResult(); PublishGameAutomation(); }
    }

    private async Task StopRequestedCaptureAsync()
    {
        bool manualWasActive = _automaticGame == null && CaptureActive;
        await QuiesceGameAutomationAsync();
        if (manualWasActive && _gameSuppressionStore != null && _settings.GameDetectionEnabled)
        {
            try { await ReserveManualGameSessionsAsync(); }
            catch
            {
                _gameStateUnavailable = true;
                _gameTimer?.Stop();
                PublishGameAutomation("Automatic replay is paused because the manual-stop state could not be saved. Review Games settings before restarting Flick.");
            }
        }
        await StopCaptureManagedAsync();
        _manualReplayReserved = false;
        if (_automaticGame != null)
        {
            ClearAutomaticGame();
            PublishGameAutomation("Automatic replay is paused for this game session. Start manually or restart the game.");
        }
    }

    private async Task StopCaptureManagedAsync()
    {
        if (_stopCaptureTask is { } existingStop)
        {
            await existingStop;
            return;
        }
        var stop = StopCaptureCoreAsync();
        _stopCaptureTask = stop;
        try { await stop; }
        finally
        {
            if (ReferenceEquals(_stopCaptureTask, stop)) _stopCaptureTask = null;
            PublishDiagnosticTest();
        }
    }

    private async Task StopCaptureCoreAsync()
    {
        _captureEpoch++;
        CaptureStatus = "Stopping";
        bool stopped = false;
        try { stopped = await _captureHost.StopCaptureAsync(); }
        catch (Exception ex) { RecordCaptureFailure("stop-failed", "Capture stop error: " + ex.Message); }
        finally
        {
            await _captureHost.ShutdownAsync();
            CaptureStatus = "Off";
            IsArmed = false;
            _health.ObserveEngine(_healthSession, RecordingEngineState.Stopped);
            _health.ObserveHost(_healthSession, CaptureHostHealth.Exited);
            BufferedDurationText = $"0.0s / {_settings.ReplayDurationSeconds}.0s";
            if (stopped) StatusDetails = "Capture stopped; native helper exited.";
            else RecordCaptureFailure("forced-stop", "Capture was force-terminated. Check the previous clip before restarting.", false);
            PublishHealth();
        }
    }

    public async Task<bool> PrepareToQuitAsync()
    {
        ClearFilmstrip();
        _thumbnailCancellation?.Cancel();
        _isPreparingToQuit = true;
        PublishDiagnosticTest();
        // A timer tick must not restart an exited helper during finalization.
        _pollTimer.Stop();
        PauseResourceSampling();
        _gameTimer?.Stop();
        await QuiesceGameAutomationAsync();
        bool ready = false;
        try
        {
            if (!await StopSelectedThumbnailAsync(allowSettledShutdown: true)) return false;
            if (!await CancelAudioPreviewAndWaitAsync(allowSettledShutdown: true)) return false;
            if (!await StopFilmstripAsync(allowUnconfirmedShutdown: true)) return false;

            CancelRecordingDiagnostic();
            if (_recordingDiagnosticCompletion is { } test)
            {
                try { await test.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
                catch (TimeoutException) { return false; }
            }
            // An active ZIP owns a temporary file. Cancel and await its cleanup
            // before allowing the process to exit; a slow I/O cancellation
            // refuses this quit attempt rather than abandoning that operation.
            _diagnosticExportCancellation?.Cancel();
            if (_diagnosticExportCompletion is { } export)
            {
                try { await export.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { return false; }
            }
            await StopCaptureAsync(DiagnosticControlOrigin.Quit);
            var deadline = Stopwatch.StartNew();
            while (IsReplaySavePending && deadline.Elapsed < TimeSpan.FromSeconds(30))
                await Task.Delay(100);
            // Settle optional reads after capture cleanup; resource display
            // must never delay releasing the recording source or microphone.
            if (!await StopResourceSamplingAsync()) return false;
            ready = !IsReplaySavePending;
            if (ready && _diagnosticJournal != null)
            {
                using var flushDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await _diagnosticJournal.FlushAsync(flushDeadline.Token); }
                catch (OperationCanceledException) { } // Logs cannot prevent a normal quit.
            }
            return ready;
        }
        finally
        {
            if (!ready) { _isPreparingToQuit = false; ResumeResourceSampling(); _pollTimer.Start(); if (_settings.GameDetectionEnabled) _gameTimer?.Start(); PublishDiagnosticTest(); }
        }
    }

    public async Task SaveReplayAsync(DiagnosticControlOrigin origin = DiagnosticControlOrigin.Unknown)
    {
        if (_isRecordingDiagnosticRunning || _isPreparingToQuit) return;
        RecordReplayControl(origin, DiagnosticControlAction.Save);
        if (_captureHost.HasPendingReplaySaves || _saveRequestOperations > 0)
        {
            // Native replay writes are single-flight. A request already
            // being accepted must not produce a misleading second red toast.
            StatusDetails = "A clip is already saving. Flick is awaiting its result; the replay buffer remains active.";
            return;
        }
        if (CaptureStatus != "Armed" || !IsArmed || _health.Snapshot.EngineState != RecordingEngineState.Ready)
        {
            StatusDetails = CaptureStatus switch
            {
                "Off" => $"Replay is off. Start replay ({_settings.CaptureToggleHotkey}) before saving a clip.",
                "Starting" => "Replay is starting. Wait until Replay Ready before saving.",
                "Faulted" when !string.IsNullOrWhiteSpace(StatusDetails) => StatusDetails,
                "Faulted" => "Replay capture failed. Review the capture error and start replay again.",
                _ => "Replay is not ready to save. Start replay and wait until Replay Ready."
            };
            _health.RecordSaveIssue("not-ready", StatusDetails);
            RecordDiagnostic(DiagnosticEventKind.SaveFailed, DiagnosticErrorCode.NotReady);
            PublishHealth();
            ReplaySaveFailed?.Invoke(StatusDetails);
            return;
        }

        string saveId = "clip_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N");
        var saveSession = _healthSession;
        if (!_health.BeginSave(saveSession, saveId))
        {
            _health.RecordSaveIssue("health-not-ready", "Replay health is not ready. Restart replay before saving.");
            RecordDiagnostic(DiagnosticEventKind.SaveFailed, DiagnosticErrorCode.NotReady);
            PublishHealth();
            ReplaySaveFailed?.Invoke("Replay health is not ready. Restart replay before saving.");
            return;
        }
        _saveContexts.Add(saveId, (saveSession, _health.Snapshot.Source?.Name ?? SelectedTarget?.DisplayName ?? "Screen", false));
        _saveRequestOperations++;
        OnPropertyChanged(nameof(IsReplaySavePending));
        StatusDetails = "Snapshotting replay buffer to original MKV...";
        RecordDiagnostic(DiagnosticEventKind.SaveRequested);
        PublishHealth();
        try
        {
            if (!await _captureHost.SaveReplayAsync(saveId, "Clip"))
            {
                // A real terminal file may have overtaken its command ACK.
                if (_saveContexts.TryGetValue(saveId, out var pending) && !pending.Validating)
                {
                    RecordSaveFailure(saveId, _captureHost.LastSaveError, DiagnosticErrorCode.CommandRejected);
                    ReplaySaveFailed?.Invoke(StatusDetails);
                }
            }
            else
            {
                if (_saveContexts.TryGetValue(saveId, out var acknowledged) && !acknowledged.Validating &&
                    _health.AcknowledgeSave(saveSession, saveId, true))
                    RecordDiagnostic(DiagnosticEventKind.SaveAccepted);
                PublishHealth();
            }
        }
        catch (Exception ex)
        {
            // Once a real terminal file reaches validation, a late/missing ACK
            // cannot remove its health context or turn success into a failure.
            if (_saveContexts.TryGetValue(saveId, out var pending) && !pending.Validating)
            {
                StatusDetails = _captureHost.HasPendingReplaySaves
                    ? "Replay save status could not be confirmed. Flick is still tracking the save: " + ex.Message
                    : "Replay save failed: " + ex.Message;
                if (!_captureHost.HasPendingReplaySaves)
                    RecordSaveFailure(saveId, StatusDetails, DiagnosticErrorCode.HostExited);
                else
                {
                    // An IPC ACK timeout does not prove the save failed. Keep
                    // the request registered until a correlated terminal event
                    // or confirmed capture-host exit settles its outcome.
                    RecordDiagnostic(DiagnosticEventKind.HostChanged, DiagnosticErrorCode.HostUnresponsive);
                    PublishHealth();
                }
                if (!_captureHost.HasPendingReplaySaves)
                    ReplaySaveFailed?.Invoke(StatusDetails);
            }
        }
        finally
        {
            _saveRequestOperations--;
            OnPropertyChanged(nameof(IsReplaySavePending));
        }
    }

    private void OnPendingReplaySavesChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
            _ = dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(IsReplaySavePending))));
    }

    private void OnReplaySaveAborted(string reason)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
        {
            void ReportFailure()
            {
                foreach (var pending in _saveContexts.Where(p => !p.Value.Validating).Select(p => p.Key).ToArray())
                    RecordSaveFailure(pending, reason, DiagnosticErrorCode.HostExited);
                ReplaySaveFailed?.Invoke(reason);
            }
            if (dispatcher.CheckAccess()) ReportFailure();
            else _ = dispatcher.BeginInvoke(new Action(ReportFailure));
        }
    }

    private void OnHostStateChanged(string state)
    {
        var observedSession = _health.Snapshot.SessionId;
        int observedEpoch = _captureEpoch;
        int? observedHost = _captureHost.HostProcessId;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (observedSession != _healthSession || observedEpoch != _captureEpoch ||
                observedHost != _captureHost.HostProcessId) return;
            if (CaptureStatus == "Stopping" ||
                (state is "Armed" or "Buffering" && CaptureStatus is not ("Starting" or "Armed" or "Buffering"))) return;
            CaptureStatus = state;
            _health.ObserveHost(_healthSession, CaptureHostHealth.Responsive);
            PublishHealth();
            if (state == "Armed")
            {
                IsArmed = true;
                StatusDetails = "Ready to save retained replay.";
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
            _replayIndexOperations++;
            OnPropertyChanged(nameof(IsReplaySavePending));
            try
            {
                if (!_saveContexts.TryGetValue(saveId, out var context)) return;
                _saveContexts[saveId] = (context.Session, context.Target, true);
                _health.MarkSaveValidating(context.Session, saveId);
                StatusDetails = "Validating saved clip…";
                PublishHealth();
                if (File.Exists(filePath))
                {
                    try
                    {
                        // A thumbnail is optional work (up to 15s). Confirm the
                        // playable, indexed MKV and notify before creating it.
                        var clip = await _storage.RegisterNewOriginalAsync(
                            filePath, context.Target, trackNames, generateThumbnail: false);
                        // Reloading an ObservableCollection can temporarily clear the
                        // ListBox selection through its TwoWay binding. Snapshot the
                        // edit context BEFORE rebuilding the gallery.
                        var editing = SelectedClip;
                        ReloadClips();
                        SelectedClip = editing is null
                            ? clip
                            : FilteredClips.FirstOrDefault(c => c.Id == editing.Id) ?? editing;
                        _health.RecordValidatedClip(context.Session, saveId, clip.Id, clip.DurationSeconds);
                        _saveContexts.Remove(saveId);
                        RecordDiagnostic(DiagnosticEventKind.SaveValidated);
                        StatusDetails = "Clip Saved";
                        PublishHealth();
                        try { ClipSavedConfirmed?.Invoke(clip); }
                        catch (Exception notificationError)
                        {
                            StatusDetails = "Clip Saved. A notification could not be shown: " + notificationError.Message;
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordSaveFailure(saveId, "Saved clip could not be validated or indexed: " + ex.Message,
                            ex is UnauthorizedAccessException or IOException ? DiagnosticErrorCode.StorageUnavailable
                                : DiagnosticErrorCode.InvalidMedia);
                        ReplaySaveFailed?.Invoke(StatusDetails);
                    }
                }
                else
                {
                    RecordSaveFailure(saveId, "The capture helper reported a saved clip, but its original file is unavailable.",
                        DiagnosticErrorCode.StorageUnavailable);
                    ReplaySaveFailed?.Invoke(StatusDetails);
                }
            }
            finally
            {
                _replayIndexOperations--;
                OnPropertyChanged(nameof(IsReplaySavePending));
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
                string saveId = data.HasValue && data.Value.TryGetProperty("save_id", out var id) ? id.GetString() ?? "" : "";
                RecordSaveFailure(saveId, "Replay save failed: " + reason,
                    reason.Contains("timed out", StringComparison.OrdinalIgnoreCase) ? DiagnosticErrorCode.SaveTimedOut
                        : DiagnosticErrorCode.CommandRejected);
                ReplaySaveFailed?.Invoke(reason);
            }));
        // Save errors leave an otherwise healthy replay buffer armed.
    }

    private void OnHostFaultOccurred(string reason)
    {
        var observedSession = _health.Snapshot.SessionId;
        int observedEpoch = _captureEpoch;
        int? observedHost = _captureHost.HostProcessId;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (observedSession != _healthSession || observedEpoch != _captureEpoch ||
                observedHost != _captureHost.HostProcessId) return;
            CaptureStatus = "Faulted";
            IsArmed = false;
            _health.ObserveHost(_healthSession, _captureHost.IsRunning ? CaptureHostHealth.Responsive : CaptureHostHealth.Exited);
            RecordCaptureFailure("capture-fault", "Capture Fault: " + reason);
        });
    }

    private async void OnPollTick(object? sender, EventArgs e)
    {
        if (_isRecordingDiagnosticRunning || _isPreparingToQuit || _gameAutomationBusy) return;
        if (_pollInProgress || CaptureStatus is "Starting" or "Stopping") return;
        if (!_captureHost.IsRunning)
        {
            _health.ObserveHost(_healthSession, CaptureHostHealth.Exited);
            if (CaptureActive)
            {
                CaptureStatus = "Faulted";
                RecordCaptureFailure("host-exited", "The recording helper exited. Review the source and restart replay.");
            }
            return;
        }
        _pollInProgress = true;
        int epoch = _captureEpoch;
        int? hostPid = _captureHost.HostProcessId;
        try
        {
            var st = await _captureHost.GetStatusAsync();
            // A query started before stop/restart must not rewrite the new session.
            if (epoch != _captureEpoch || hostPid != _captureHost.HostProcessId) return;
            SetAudioCaptureStatus(st.Audio);
            _health.ObserveHost(_healthSession, CaptureHostHealth.Responsive);
            if (st.State == "Faulted" || !string.IsNullOrWhiteSpace(st.FaultReason) || !st.TargetValid)
            {
                bool alreadyFaulted = CaptureStatus == "Faulted";
                CaptureStatus = "Faulted";
                if (!alreadyFaulted || _health.Snapshot.LastRecordingIssue == null)
                    RecordCaptureFailure("target-or-engine-fault", st.FaultReason switch {
                        "capture_target_lost" => "Selected capture source was lost. Choose an available window or monitor and restart replay.",
                        "capture_buffer_tracking_failed" => "Replay buffer tracking failed. Restart replay before saving.",
                        "capture_save_timed_out" => "Replay save timed out. Check the previous clip and restart replay.",
                        _ => "The recording engine reported a fault. Check the selected source and encoder, then restart replay."
                    });
            }
            else if (CaptureStatus != "Faulted" && st.State is ("Buffering" or "Armed"))
            {
                // Inconsistent readiness is not permission to save.
                CaptureStatus = st.State == "Armed" && st.IsArmed ? "Armed" : "Buffering";
                if (st.Profile != null) SetCaptureProfile(st.Profile);
                BufferedDurationText = $"{(st.BufferedDurationEstimated ? "~" : "")}{st.BufferedSec:F1}s / {st.MaxSec:F0}s";
                _health.ObserveBuffer(_healthSession, st.BufferedSec, st.MaxSec,
                    st.BufferedDurationEstimated, st.BufferTrackingLimited);
            }
            else if (CaptureActive && st.State == "Off")
            {
                CaptureStatus = "Faulted";
                RecordCaptureFailure("unexpected-stop", "The recording engine stopped unexpectedly. Review the source and restart replay.");
            }
            if (st.PrivateBytes > 805_306_368L)
            {
                await StopCaptureAsync();
                RecordCaptureFailure("host-memory-limit", "Capture stopped: native host exceeded the 768 MiB safety limit.", false);
                return;
            }
            PublishHealth();
        }
        catch (Exception ex)
        {
            if (epoch == _captureEpoch && hostPid == _captureHost.HostProcessId)
            {
                // Disable saves until the engine responds with valid readiness.
                IsArmed = false;
                _health.ObserveHost(_healthSession, CaptureHostHealth.Unresponsive);
                RecordCaptureFailure("host-unresponsive", "Capture health unavailable: " + ex.Message, false);
            }
        }
        finally
        {
            _pollInProgress = false;
            OnPropertyChanged(nameof(CanRunRecordingDiagnostic));
            OnPropertyChanged(nameof(CanInvokeRecordingDiagnostic));
        }
    }

    public async Task<ClipImportSummary> ImportFolderAsync(
        string folderPath, bool includeSubfolders = true)
    {
        if (IsCaptureActive || IsExporting)
            throw new InvalidOperationException("Finish the active replay or export before importing. Gameplay and sharing take priority.");
        if (IsImporting)
            throw new InvalidOperationException("An import is already in progress.");
        var folder = _storage.ConnectFolder(folderPath, includeSubfolders);
        RefreshConnectedFolders();
        return await RescanConnectedFolderAsync(folder.Id);
    }

    public async Task<ClipImportSummary> RescanConnectedFolderAsync(string folderId)
    {
        if (IsCaptureActive || IsExporting)
            throw new InvalidOperationException("Finish the active replay or export before scanning external clips.");
        if (IsImporting)
            throw new InvalidOperationException("A folder is already being scanned.");

        using var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;
        IsImporting = true;
        ImportStatusText = "Finding videos…";
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
                ? "Folder unavailable · clips kept"
                : $"Complete · {result.Imported:N0} Added, " +
                  $"{result.AlreadyIndexed:N0} Already Indexed, {result.Failed:N0} Unavailable" +
                  (result.ScanLimitReached ? " · 10,000-File Safety Limit Reached; Use Smaller Folders" : "");
            return result;
        }
        catch (OperationCanceledException)
        {
            ImportStatusText = "Scan cancelled · indexed clips kept";
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
            ImportStatusText = "Folder unlinked · videos kept";
        }
        return removed;
    }

    public async Task RescanLibraryAsync()
    {
        if (IsCaptureActive || IsExporting)
        {
            StatusDetails = "Finish replay or export before a full library rescan.";
            return;
        }
        if (IsImporting || Volatile.Read(ref _libraryRescanOperations) > 0)
        {
            StatusDetails = "A library scan is already running.";
            return;
        }

        Interlocked.Increment(ref _libraryRescanOperations);
        try
        {
            OnPropertyChanged(nameof(IsBackgroundWorkActive));
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
        finally
        {
            try { ReloadClips(); }
            finally
            {
                Interlocked.Decrement(ref _libraryRescanOperations);
                OnPropertyChanged(nameof(IsBackgroundWorkActive));
            }
        }
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
        bool wasReady = SelectedClip.IsExportReady;
        SelectedClip.TrimStartSeconds = validated.ValidatedStart;
        SelectedClip.TrimEndSeconds = validated.ValidatedEnd;
        try
        {
            SaveClipEdits();
            RememberTrim(oldStart, oldEnd);
            EditorSaveState = "Trim saved · original unchanged";
            StatusDetails = "Trim saved";
            return true;
        }
        catch (Exception ex)
        {
            SelectedClip.TrimStartSeconds = oldStart;
            SelectedClip.TrimEndSeconds = oldEnd;
            SelectedClip.IsExportReady = wasReady;
            OnSelectedClipChanged();
            EditorSaveState = "Couldn't save trim · previous range restored";
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
        _audioMixRevision++;
        OnPropertyChanged(nameof(AudioMixRevision));
        if (SelectedClip is null || !SelectedClip.IsAvailable) return;
        InvalidateExportReady(); // Never let stale exported audio look ready.
        _pendingAudioClip = SelectedClip;
        EditorSaveState = "Saving audio mix…";
        _audioEditCommitTimer.Stop();
        _audioEditCommitTimer.Start();
    }

    public void FlushPendingAudioEdits()
    {
        _audioEditCommitTimer?.Stop();
        var clip = _pendingAudioClip;
        _pendingAudioClip = null;
        if (clip is null) return;
        try
        {
            _storage.AddOrUpdateClip(clip);
            if (ReferenceEquals(clip, SelectedClip)) EditorSaveState = "Audio mix saved · original unchanged";
        }
        catch (Exception ex)
        {
            StatusDetails = "Couldn't Save Audio Mix: " + ex.Message;
            if (ReferenceEquals(clip, SelectedClip)) EditorSaveState = StatusDetails;
        }
    }

    public void ResetAudioMix()
    {
        if (SelectedClip == null || IsExporting || !SelectedClip.IsAvailable) return;
        LocalClip.Core.Audio.AudioTrackRoles.Reset(SelectedClip);
        ScheduleAudioEdit();
    }

    public void ReloadClips()
    {
        string? selectedId = SelectedClip?.Id;
        _allClips = _storage.GetAllClips();
        foreach (var clip in _allClips) clip.RefreshAvailability();
        RefreshOrganizationFacets();
        ApplyFilter();
        if (selectedId != null)
        {
            var previous = _allClips.FirstOrDefault(c => c.Id == selectedId);
            if (previous == null)
                SelectedClip = null; // Never keep editing a removed/unlinked entry.
            else if (!ReferenceEquals(SelectedClip, previous))
                SelectedClip = previous;
            else
                OnSelectedClipChanged(); // Availability may change on the same cached metadata instance.
        }
        UpdateLibrarySummary();
    }

    public void ApplyFilter()
    {
        DateTime? after = LibraryPeriod switch { "7 days" => DateTime.Now.AddDays(-7), "30 days" => DateTime.Now.AddDays(-30), _ => null };
        var visible = ClipLibraryQuery.Apply(_allClips, _searchQuery, _clipSourceFilter, FavoritesOnly, GameFilter, LibrarySort, after);
        if (LibrarySort == "Recently Exported") visible = visible.Where(clip => _recentExportIds.Contains(clip.Id)).ToList();
        string? previousGame = null;
        foreach (var clip in visible)
        {
            clip.ShowGameHeading = LibrarySort == "Game" && !string.Equals(previousGame, clip.GameGroup, StringComparison.OrdinalIgnoreCase);
            previousGame = clip.GameGroup;
        }
        _applyingLibraryFilter = true;
        try { _filteredClips.ReplaceAll(visible); }
        finally { _applyingLibraryFilter = false; }
    }

    private void OnSelectedClipChanged()
    {
        // Reopening Share or rescanning can change availability without changing
        // the selected metadata object. Refresh its derived command bindings too.
        OnPropertyChanged(nameof(CanEditClip));
        OnPropertyChanged(nameof(CanStartExport));
        if (SelectedClip == null)
        {
            InvalidateExportReady();
            return;
        }
        if (!SelectedClip.IsAvailable)
        {
            InvalidateExportReady();
            ExportStatusMessage = "Original video offline · reconnect its folder";
            return;
        }

        if (SelectedClip.IsAvailable && CanGenerateSelectedThumbnail &&
            (string.IsNullOrWhiteSpace(SelectedClip.ThumbnailPath) ||
             !File.Exists(SelectedClip.ThumbnailPath)))
            _ = EnsureSelectedThumbnailAsync(SelectedClip);

        // A Ready tile requires both the matching current edits and actual MP4 bytes.
        if (ReadyShareFile.TryGet(SelectedClip, TargetSizeMb, out var ready, _settings.ExportsFolder))
        {
            IsExportReady = true;
            ReadyExportFilePath = ready;
            ExportStatusMessage = $"Ready to share · {SelectedClip.ExportedFileSizeBytes / 1_000_000.0:F2} MB";
        }
        else
        {
            // Switching clips or a size preset must not permanently destroy a
            // verified old export: if the user switches back to its original
            // cap, the ready MP4 may be reused without wasting an encode.
            IsExportReady = false;
            ReadyExportFilePath = "";
            ExportStatusMessage = "Export to prepare an MP4.";
        }
    }

    private async Task EnsureSelectedThumbnailAsync(ClipMetadata clip)
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _thumbnailCancellation, cancellation);
        previous?.Cancel();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thumbnailOperations.Add(completion.Task);
        OnPropertyChanged(nameof(IsBackgroundWorkActive));
        bool acquired = false;
        try
        {
            await _thumbnailGate.WaitAsync(cancellation.Token);
            acquired = true;
            if (!CanGenerateSelectedThumbnail || SelectedClip?.Id != clip.Id) return;
            await _storage.EnsureThumbnailAsync(clip.Id, cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(clip, SelectedClip)) NotifyShareThumbnailAvailable(clip);
        }
        catch (OperationCanceledException) { }
        catch (MediaWorkerExitUnconfirmedException)
        {
            _thumbnailCleanupUnconfirmed = true;
            StatusDetails = "Thumbnail worker exit was not confirmed. Quit and reopen Flick before recording or exporting.";
            PublishDiagnosticTest(); OnPropertyChanged(nameof(CanStartExport));
        }
        catch (Exception) { /* Keep clip playable even without a thumbnail. */ }
        finally
        {
            if (acquired) _thumbnailGate.Release();
            if (ReferenceEquals(_thumbnailCancellation, cancellation))
                _thumbnailCancellation = null;
            cancellation.Dispose();
            _thumbnailOperations.Remove(completion.Task);
            if (_thumbnailOperations.Count == 0) _thumbnailStopPending = false;
            completion.TrySetResult();
            PublishDiagnosticTest(); OnPropertyChanged(nameof(CanStartExport));
            OnPropertyChanged(nameof(IsBackgroundWorkActive));
        }
    }

    public async Task<bool> StopSelectedThumbnailAsync(bool allowSettledShutdown = false, TimeSpan? cleanupBudget = null)
    {
        _thumbnailCancellation?.Cancel();
        var pending = _thumbnailOperations.ToArray();
        _thumbnailStopPending = pending.Length != 0;
        PublishDiagnosticTest(); OnPropertyChanged(nameof(CanStartExport));
        try { await Task.WhenAll(pending).WaitAsync(cleanupBudget ?? TimeSpan.FromSeconds(6)); }
        catch (TimeoutException) { return false; }
        return _thumbnailOperations.Count == 0 && (allowSettledShutdown || !_thumbnailCleanupUnconfirmed);
    }

    public void InvalidateExportReady()
    {
        IsExportReady = false;
        ReadyExportFilePath = "";
        ExportStatusMessage = "Clip changed · export again.";
        if (SelectedClip != null)
        {
            bool hadReadyExport = SelectedClip.IsExportReady;
            SelectedClip.IsExportReady = false;
            if (hadReadyExport) { RefreshRecentExports(); if (LibrarySort == "Recently Exported") ApplyFilter(); }
        }
    }

    public async Task ExportForDiscordAsync()
    {
        if (!CanStartExport || SelectedClip is null) return;
        ClearFilmstrip();
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
        ExportStatusMessage = "Preparing MP4…";
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
            if (!await StopSelectedThumbnailAsync()) throw new InvalidOperationException("Thumbnail work is still stopping. Wait before exporting.");
            if (!await StopFilmstripAsync()) throw new InvalidOperationException("Preview frames are still stopping. Wait before exporting.");
            ExportResult result = await _exporter.ExportForDiscordAsync(
                clip, snapshot, progress, cancellation.Token);
            if (result.Success)
            {
                clip.ExportedFilePath = result.ExportFilePath;
                clip.ExportedFileSizeBytes = result.FileSizeBytes;
                clip.LastExportFingerprint = result.Fingerprint;
                clip.IsExportReady = true;
                clip.LastExportedAtUtc = DateTimeOffset.UtcNow;
                clip.LastExportTargetMb = snapshot.DiscordTargetSizeMb;
                _storage.AddOrUpdateClip(clip);
                RefreshRecentExports();
                double mb = result.FileSizeBytes / 1_000_000d;
                if (SelectedClip?.Id == clip.Id &&
                    _targetSizeMb == snapshot.DiscordTargetSizeMb)
                {
                    ReadyExportFilePath = result.ExportFilePath;
                    IsExportReady = ReadyShareFile.TryGet(clip, TargetSizeMb, out _, _settings.ExportsFolder);
                    ExportProgress = 100;
                    ExportStatusMessage = IsExportReady
                        ? $"Ready to share · {mb:F2} MB"
                        : "Export verification failed";
                }
                else
                {
                    ExportStatusMessage = $"Exported {clip.Title} · Select It to Share";
                    OnSelectedClipChanged();
                }
            }
            else
            {
                ExportStatusMessage = "Export failed: " + result.ErrorMessage;
                StatusDetails = ExportStatusMessage;
            }
        }
        catch (OperationCanceledException)
        {
            ExportStatusMessage = "Export cancelled · original unchanged";
            ExportProgress = 0;
        }
        catch (Exception ex)
        {
            ExportStatusMessage = "Export failed: " + ex.Message;
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
            ExportStatusMessage = "File copied · paste into Discord";
        }
        catch (Exception ex) { ExportStatusMessage = "Couldn't copy file: " + ex.Message; }
    }

    public void OpenExportFolder()
    {
        string? directory = TryGetReadyShareFile(out var path)
            ? Path.GetDirectoryName(path) : _settings.ExportsFolder;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            StatusDetails = "Export folder unavailable · check Storage";
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
            : $"Delete '{SelectedClip.Title}'? The original video will be sent to the Recycle Bin. Any associated exported MP4 may be permanently deleted.";
        var answer = LocalClip.App.Services.FlickDialog.Show(question,
            imported ? "Remove from library" : "Delete original video",
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
        try
        {
            if (!_storage.RenameClip(SelectedClip.Id, newTitle.Trim()))
            {
                StatusDetails = "Clip could not be found for renaming.";
                return;
            }
            ReloadClips();
            StatusDetails = "Clip renamed.";
        }
        catch (Exception ex)
        {
            // A locked, damaged or inaccessible metadata index should report
            // an error, never terminate Flick through the WPF click handler.
            StatusDetails = "Couldn't rename clip: " + ex.Message;
        }
    }

    public async Task SendToDiscordWebhookAsync(string message = "")
    {
        if (!_settings.EnableDiscordWebhook)
        {
            StatusDetails = "Direct posting is off · enable it in Sharing";
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
            LocalClip.App.Services.FlickDialog.Show("No Discord Webhook URL is configured. Open Settings to set one up.", "Webhook not configured", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var r = LocalClip.App.Services.FlickDialog.Show($"Send this clip ({Path.GetFileName(ReadyExportFilePath)}) to channel '{_settings.DiscordWebhookChannelName}'?", "Send to Discord?", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        Interlocked.Increment(ref _discordUploadOperations);
        try
        {
            OnPropertyChanged(nameof(IsBackgroundWorkActive));
            ExportStatusMessage = "Posting clip to Discord webhook...";
            var client = new DiscordWebhookClient();
            var res = await client.PostClipAsync(secretUrl, safeFile, message);

            if (res.Success)
            {
                ExportStatusMessage = "Successfully posted to Discord!";
                LocalClip.App.Services.FlickDialog.Show("Clip posted successfully!", "Discord webhook", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                ExportStatusMessage = "Post failed: " + res.ErrorMessage;
                LocalClip.App.Services.FlickDialog.Show(res.ErrorMessage, "Discord webhook error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _discordUploadOperations);
            OnPropertyChanged(nameof(IsBackgroundWorkActive));
        }
    }

    public void Dispose()
    {
        DisposeSharePresentation();
        ClearFilmstrip();

        DisposeGameAutomation();

        _exportCancellation?.Cancel();
        FlushPendingAudioEdits();
        UnsubscribeAudioEvents();
        _importCancellation?.Cancel();
        _thumbnailCancellation?.Cancel();
        _pollTimer.Stop();
        DisposeResourceSampling();
        _captureHost.CaptureConfigured -= OnCaptureConfigured;
        _captureHost.PendingReplaySavesChanged -= OnPendingReplaySavesChanged;
        _captureHost.ReplaySaveAborted -= OnReplaySaveAborted;
        _captureHost.StateChanged -= OnHostStateChanged;
        _captureHost.ReplaySavedWithTracks -= OnHostReplaySaved;
        _captureHost.FaultOccurred -= OnHostFaultOccurred;
        _captureHost.EventReceived -= OnHostEventReceived;
        _captureHost.Dispose();
        _diagnosticsDisposed = true;
        if (_recordingDiagnosticCancellation is { } testCancellation)
            _recordingDiagnosticCancellationDispatch = DispatchDiagnosticCancellationAsync(testCancellation);
        _diagnosticExportCancellation?.Cancel();
        // Normal quit flushes asynchronously. Disposal is also used by unshown
        // fixtures, which never open a journal. The writer owns no UI context.
        if (_diagnosticJournal != null) _ = _diagnosticJournal.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        OnShareDependencyChanged(name);
        if (name is nameof(IsCaptureActive) or nameof(CaptureStatus) or nameof(CanEditClip) or nameof(IsExporting) or nameof(IsBackgroundWorkActive))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLoadFilmstrip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUndoTrim)));
        }

        if (name is nameof(IsBackgroundWorkActive) or nameof(IsReplaySavePending) or nameof(IsImporting) or nameof(IsExporting))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRunRecordingDiagnostic)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanInvokeRecordingDiagnostic)));
        }
    }
}
