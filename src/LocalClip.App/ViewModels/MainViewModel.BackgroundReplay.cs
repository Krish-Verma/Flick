using System.IO;
using System.Threading;
using LocalClip.Core.GameDetection;

namespace LocalClip.App.ViewModels;

public partial class MainViewModel
{
    private readonly AutomaticReplaySession _automaticReplaySession = new();
    private bool _backgroundReplayOwned;
    private bool _backgroundReplayStarting;
    private bool _backgroundReplayStartFailed;
    private bool _automaticReplayResumeInProgress;
    private readonly SemaphoreSlim _automaticReplayIntentGate = new(1, 1);
    private Task<bool>? _automaticReplayResumeTask;
    private int _automaticReplayIntentRevision;
    private GameDetectionReason _lastGameDetectionReason = GameDetectionReason.Disabled;
    private string _gameReplayWaitingHint = "";
    private bool HasBlockingGamePause => AutomaticReplaySession.HasBlockingGamePause(
        _settings.GameDetectionEnabled, _settings.ApprovedGameProfiles, _gameSuppressed);
    public bool BackgroundReplayEnabled => _automaticReplaySession.BackgroundEnabled;
    public bool IsAutomaticReplayPaused => _automaticReplaySession.BackgroundPaused ||
        (HasBlockingGamePause && (_automaticReplaySession.BackgroundEnabled ||
            _lastGameDetectionReason == GameDetectionReason.Suppressed));
    public bool HasAutomaticReplayStartFailure => _gameStateUnavailable || _backgroundReplayStartFailed ||
        _gameReplayWaitingHint == "Start replay to retry";
    public string ReplayWaitingText => CaptureActive ? "" : _settings.RequiresExplicitSettingsSave ? "Review Settings" :
        _gameStateUnavailable ? "Automatic replay unavailable" :
        IsAutomaticReplayPaused ? "Replay paused" :
        _backgroundReplayStartFailed ? "Start replay to retry" :
        _settings.GameDetectionEnabled && _gameReplayWaitingHint.Length > 0 ? _gameReplayWaitingHint :
        !_automaticReplaySession.BackgroundEnabled ? "Replay off" :
        BackgroundSourceKey() == null ? "Choose a source" : "Waiting to start replay";

    /// <summary>Called by the opened app on a tray/startup transition, never by fixture construction.</summary>
    public async Task ApplyBackgroundReplayPreferencesAsync(bool enabled)
    {
        _automaticReplaySession.ConfigureBackground(enabled);
        // Explicit launch/tray/settings actions may retry corrected preferences;
        // timer ticks never clear a failed attempt or a deliberate user pause.
        _automaticReplaySession.ResetBackgroundAttempt();
        _backgroundReplayStartFailed = false;
        if (!enabled && _backgroundReplayOwned)
        {
            // This preference controls future automatic starts. Turning it off
            // must not discard an existing replay buffer without a Stop action.
            _backgroundReplayOwned = false;
            _manualReplayReserved = CaptureActive;
        }
        UpdateAutomaticReplayTimer();
        PublishGameAutomation();
        if (enabled)
        {
            await RunAutomaticReplayTickAsync();
            await TryStartBackgroundReplayAsync();
        }
    }

    /// <summary>Only an explicit user Stop calls this; normal Quit/source changes do not create a pause.</summary>
    public async Task PauseAutomaticReplayAsync()
    {
        Interlocked.Increment(ref _automaticReplayIntentRevision);
        _automaticReplaySession.PauseBackground();
        var active = _automaticGame?.Process;
        await QuiesceGameAutomationAsync();
        await _automaticReplayIntentGate.WaitAsync();
        try { await ReserveManualGameSessionsAsync(active); }
        catch
        {
            // A pause-state failure must never prevent Stop from actually stopping.
            _gameStateUnavailable = true;
            _gameTimer?.Stop();
            PublishGameAutomation("Automatic replay paused. Review Games settings to resume.");
        }
        finally { _automaticReplayIntentGate.Release(); }
        _backgroundReplayOwned = false;
    }

    /// <summary>Explicit Start updates pause intent only. The caller starts manual replay separately.</summary>
    public async Task<bool> ResumeAutomaticReplayAsync()
    {
        if (_automaticReplayResumeTask is { } pending) return await pending;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _automaticReplayResumeTask = completion.Task;
        int revision = Interlocked.Increment(ref _automaticReplayIntentRevision);
        bool resumed = false, gateHeld = false;
        _automaticReplayResumeInProgress = true;
        try
        {
            await QuiesceGameAutomationAsync();
            _gameAutomationBusy = true;
            PublishGameAutomation();
            await _automaticReplayIntentGate.WaitAsync();
            gateHeld = true;
            if (revision != _automaticReplayIntentRevision) return false;
            if (GameDetectionPolicy.ShouldScan(_settings.GameDetectionEnabled, _settings.ApprovedGameProfiles))
            {
                _gameSuppressionStore ??= new GameSuppressionStore(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalClip", "game-replay-state.json"));
                var saved = await Task.Run(() => _gameSuppressionStore.Load());
                var profiles = _settings.ApprovedGameProfiles.ToArray();
                var snapshot = await Task.Run(() => _gameEnumerator.Scan(CancellationToken.None));
                if (snapshot.Status != GameScanStatus.Complete || !profiles.SequenceEqual(_settings.ApprovedGameProfiles))
                    throw new InvalidOperationException("Running games changed during resume.");
                var verified = await Task.Run(() => snapshot.Windows.Where(window => window.Eligible && profiles.Any(profile =>
                        profile.Enabled && string.Equals(profile.ExecutablePath.Replace('/', '\\'),
                            window.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                    .Select(window =>
                    {
                        var observed = _gameEnumerator.Revalidate(window);
                        return observed?.Eligible == true && observed.Hwnd == window.Hwnd &&
                            observed.Process.Matches(window.Process) ? observed : null;
                    }).OfType<GameWindowObservation>().ToArray());
                if (!profiles.SequenceEqual(_settings.ApprovedGameProfiles))
                    throw new InvalidOperationException("Game preferences changed during resume.");
                var current = saved.Concat(_gameSuppressed).GroupBy(identity => identity.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()).ToArray();
                var retained = AutomaticReplaySession.PausesAfterExplicitResume(current, profiles,
                    new(GameScanStatus.Complete, verified));
                if (revision != _automaticReplayIntentRevision) return false;
                await Task.Run(() => _gameSuppressionStore.Save(retained));
                var readback = await Task.Run(() => _gameSuppressionStore.Load());
                if (!retained.Select(identity => identity.Key).Order(StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(readback.Select(identity => identity.Key).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                    throw new IOException("Automatic replay pause state did not match the saved intent.");
                if (revision != _automaticReplayIntentRevision) return false;
                _gameSuppressed.Clear();
                _gameSuppressed.AddRange(readback);
            }
            _automaticReplaySession.ResumeBackground();
            _automaticReplaySession.ResetBackgroundAttempt();
            _automaticReplaySession.RetryFailedGames();
            _backgroundReplayStartFailed = false;
            _gameStateUnavailable = false;
            _gameReplayWaitingHint = "";
            UpdateAutomaticReplayTimer();
            resumed = true;
            return resumed;
        }
        catch
        {
            // A failed state write must not block an explicit manual Start or
            // silently advertise automatic resume as saved.
            _gameStateUnavailable = true;
            _gameTimer?.Stop();
            PublishGameAutomation("Manual replay is available. Automatic resume couldn't be saved.");
            return false;
        }
        finally
        {
            if (gateHeld) _automaticReplayIntentGate.Release();
            _automaticReplayResumeInProgress = false;
            _gameAutomationBusy = false;
            _automaticReplayResumeTask = null;
            completion.TrySetResult(resumed);
            PublishGameAutomation();
        }
    }

    private string? BackgroundSourceKey()
    {
        if (SelectedTarget is not { } source || !Targets.Contains(source)) return null;
        return source.Type == "Monitor"
            ? $"Monitor|{source.MonitorIndex}|{source.MonitorDevice}"
            : $"Window|{source.WindowHwnd}|{source.WindowPid}|{source.ProcessCreationFileTime}|{source.ExecutablePath}";
    }

    private async Task TryStartBackgroundReplayAsync()
    {
        string? source = BackgroundSourceKey();
        bool blocked = _automaticReplayResumeInProgress || _backgroundReplayStarting || _gameOperationTask != null || _gameAutomationDisposed ||
            _isPreparingToQuit || _gameStateUnavailable || _manualStopOperation != null || _automaticGame != null ||
            HasBlockingGamePause ||
            _isRecordingDiagnosticRunning || _diagnosticCleanupUnconfirmed || _refreshingTargets || _pollInProgress ||
            _stopCaptureTask != null || IsReplaySavePending || IsImporting || IsExporting || IsBackgroundWorkActive ||
            DiagnosticReservationAllowed?.Invoke() == false;
        if (!_automaticReplaySession.CanTryBackground(source, CaptureActive, blocked, _settings.RequiresExplicitSettingsSave)) return;
        _automaticReplaySession.RecordBackgroundAttempt(source!);
        _backgroundReplayStarting = true;
        _gameAutomationBusy = true;
        PublishGameAutomation("Starting background replay.");
        try
        {
            // The normal start path performs the same exact-window and audio
            // validation as a user start. It never substitutes another source.
            await StartCaptureAsync();
            _backgroundReplayOwned = CaptureActive && _automaticReplaySession.BackgroundEnabled &&
                !_automaticReplaySession.BackgroundPaused;
            _backgroundReplayStartFailed = !CaptureActive && !_automaticReplaySession.BackgroundPaused;
            PublishGameAutomation(CaptureActive ? (_backgroundReplayOwned ? "Replay active in the tray." : "Replay active.")
                : "Background replay couldn't start. Choose a source or start replay to retry.");
        }
        finally
        {
            _manualReplayReserved = CaptureActive && !_automaticReplaySession.BackgroundEnabled;
            _gameAutomationBusy = false;
            _backgroundReplayStarting = false;
            PublishGameAutomation();
        }
    }

    private void UpdateAutomaticReplayTimer()
    {
        _gameTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _gameTimer.Tick -= GameTimerTick;
        _gameTimer.Tick += GameTimerTick;
        if (!_gameAutomationDisposed && !_isPreparingToQuit && !_gameStateUnavailable && !_settings.RequiresExplicitSettingsSave &&
            (_automaticReplaySession.BackgroundEnabled ||
             GameDetectionPolicy.ShouldScan(_settings.GameDetectionEnabled, _settings.ApprovedGameProfiles)))
            _gameTimer.Start();
        else _gameTimer.Stop();
    }
}
