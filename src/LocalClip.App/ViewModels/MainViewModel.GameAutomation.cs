using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.GameDetection;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;

namespace LocalClip.App.ViewModels;

public partial class MainViewModel
{
    private DispatcherTimer? _gameTimer;
    private IGameWindowEnumerator _gameEnumerator = new WindowsGameWindowEnumerator();
    private GameSuppressionStore? _gameSuppressionStore;
    private readonly List<GameProcessIdentity> _gameSuppressed = [];
    private Task? _gameOperationTask;
    private Task? _manualStopOperation;
    private bool _manualReplayReserved;
    private CancellationTokenSource? _gameOperationCancellation;
    private bool _gameAutomationBusy, _gameAutomationDisposed, _gameStateUnavailable;
    private GameWindowObservation? _automaticGame;
    private ApprovedGameProfile? _automaticGameProfile;
    private string? _appliedGamePreferences;
    private int _gamePreferencesRevision;
    private string _gameDetectionStatus = "Automatic game replay is off.";
    public string GameDetectionStatus => _gameDetectionStatus;
    public bool IsAutomaticGameCapture => _automaticGame != null;
    public string AutomaticGameSourceText => _automaticGameProfile == null ? "" : "Auto · " + _automaticGameProfile.Name;
    public int ActiveReplayDurationSeconds => _automaticGameProfile?.ReplayDurationSeconds ?? _settings.ReplayDurationSeconds;

    private void PublishGameAutomation(string? status = null)
    {
        if (status != null) _gameDetectionStatus = status;
        OnPropertyChanged(nameof(GameDetectionStatus)); OnPropertyChanged(nameof(IsAutomaticGameCapture));
        OnPropertyChanged(nameof(AutomaticGameSourceText)); OnPropertyChanged(nameof(ActiveReplayDurationSeconds));
        OnPropertyChanged(nameof(BackgroundReplayEnabled)); OnPropertyChanged(nameof(IsAutomaticReplayPaused));
        OnPropertyChanged(nameof(HasAutomaticReplayStartFailure)); OnPropertyChanged(nameof(ReplayWaitingText));
        PublishDiagnosticTest();
    }

    public async Task ApplyGameDetectionPreferencesAsync()
    {
        if (_settings.RequiresExplicitSettingsSave)
        {
            _gameTimer?.Stop();
            PublishGameAutomation("Review Settings before starting automatic replay.");
            return;
        }
        _automaticReplaySession.RetryFailedGames();
        _gameReplayWaitingHint = "";
        string preferences = JsonSerializer.Serialize(new { _settings.GameDetectionEnabled, _settings.ApprovedGameProfiles });
        if (_appliedGamePreferences == preferences && !_gameStateUnavailable)
        {
            await RunAutomaticReplayTickAsync();
            return;
        }
        _appliedGamePreferences = preferences;
        int revision = ++_gamePreferencesRevision;
        _gameTimer?.Stop();
        await QuiesceGameAutomationAsync();
        if (revision != _gamePreferencesRevision) return;
        if (_automaticGame != null)
        {
            try { await StopCaptureManagedAsync(); ClearAutomaticGame(); }
            catch
            {
                _diagnosticCleanupUnconfirmed = true;
                PublishGameAutomation("Automatic replay cleanup could not be confirmed. Close Flick before recording again.");
                return;
            }
        }
        if (_gameAutomationDisposed || _isPreparingToQuit) return;
        if (!GameDetectionPolicy.ShouldScan(_settings.GameDetectionEnabled, _settings.ApprovedGameProfiles))
        {
            _gameStateUnavailable = false;
            _lastGameDetectionReason = _settings.GameDetectionEnabled ? GameDetectionReason.NoProfiles : GameDetectionReason.Disabled;
            _gameReplayWaitingHint = _settings.GameDetectionEnabled ? "Add a game" : "";
            PublishGameAutomation(new GameDetectionDecision(_settings.GameDetectionEnabled
                ? GameDetectionReason.NoProfiles : GameDetectionReason.Disabled).StatusText);
            UpdateAutomaticReplayTimer();
            await RunAutomaticReplayTickAsync();
            return;
        }
        try
        {
            // Initialized only by the opened app/explicit Settings Save, never
            // by an unshown WPF fixture or a disabled automation constructor.
            _gameSuppressionStore ??= new GameSuppressionStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalClip", "game-replay-state.json"));
            var saved = await Task.Run(() => _gameSuppressionStore.Load());
            if (revision != _gamePreferencesRevision || _isPreparingToQuit || _gameAutomationDisposed) return;
            _gameSuppressed.Clear();
            _gameSuppressed.AddRange(saved);
            _gameStateUnavailable = false;
        }
        catch (Exception)
        {
            _gameStateUnavailable = true;
            PublishGameAutomation("Automatic game replay is paused because its saved session state could not be read safely.");
            return;
        }
        UpdateAutomaticReplayTimer();
        _gameReplayWaitingHint = "Waiting for game";
        PublishGameAutomation("Waiting for a game.");
        await RunAutomaticReplayTickAsync();
    }

    private async void GameTimerTick(object? sender, EventArgs e) => await RunAutomaticReplayTickAsync();

    private async Task RunAutomaticReplayTickAsync()
    {
        if (_settings.RequiresExplicitSettingsSave || _automaticReplayResumeInProgress || _backgroundReplayStarting || _gameOperationTask != null || _gameAutomationDisposed || _isPreparingToQuit || _gameStateUnavailable ||
            _isRecordingDiagnosticRunning || _diagnosticCleanupUnconfirmed || _refreshingTargets || _pollInProgress || _manualStopOperation != null ||
            DiagnosticReservationAllowed?.Invoke() == false) return;
        // A failed manual attempt must not retain recording ownership forever.
        if (!CaptureActive) _manualReplayReserved = false;
        if (_automaticGame == null && ((_manualReplayReserved && !_backgroundReplayOwned) ||
            (CaptureActive && !_backgroundReplayOwned) || _stopCaptureTask != null || IsReplaySavePending ||
            IsImporting || IsExporting || IsBackgroundWorkActive)) return;
        if (_backgroundReplayOwned && !CaptureActive) _backgroundReplayOwned = false;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        _gameOperationTask = completion.Task;
        _gameOperationCancellation = cancellation;
        _gameAutomationBusy = true;
        PublishGameAutomation();
        try { await RunGameTickAsync(cancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _gameStateUnavailable = true;
            if (_automaticGame != null)
            {
                try { await StopCaptureManagedAsync(); ClearAutomaticGame(); }
                catch { _diagnosticCleanupUnconfirmed = true; }
            }
            PublishGameAutomation("Automatic game replay is paused after a detection or state-storage error. Start replay manually.");
        }
        finally
        {
            _gameAutomationBusy = false;
            _gameOperationCancellation = null;
            _gameOperationTask = null;
            completion.TrySetResult();
            PublishGameAutomation();
        }
        // Starting the saved source here avoids self-awaiting the game-operation
        // reservation through StartCaptureAsync's normal quiesce path.
        await TryStartBackgroundReplayAsync();
    }

    private async Task RunGameTickAsync(CancellationToken token)
    {
        if (_automaticGame != null)
        {
            var active = _automaticGame;
            bool configured = _settings.GameDetectionEnabled && _settings.ApprovedGameProfiles.Any(profile =>
                profile.Enabled && profile.Id == _automaticGameProfile?.Id && profile == _automaticGameProfile);
            var activeSnapshot = configured ? await Task.Run(() => _gameEnumerator.Scan(token), token)
                : new GameWindowSnapshot(GameScanStatus.Complete, []);
            token.ThrowIfCancellationRequested();
            bool sameChoice = configured && _automaticGameProfile != null &&
                GameDetectionPolicy.CanContinue(_automaticGameProfile, active, activeSnapshot);
            bool helperFailed = configured && CaptureStatus is ("Faulted" or "Off");
            if (!configured || !sameChoice || helperFailed)
            {
                if (helperFailed) _automaticReplaySession.RecordGameFailure(active.Process);
                await StopCaptureManagedAsync(); ClearAutomaticGame();
                _gameReplayWaitingHint = helperFailed ? "Start replay to retry" : "Waiting for game window";
                PublishGameAutomation(helperFailed ? "Game replay couldn't continue. Start replay to retry."
                    : "Waiting for the game window.");
            }
            return;
        }
        var profiles = _settings.ApprovedGameProfiles.ToArray();
        if (!GameDetectionPolicy.ShouldScan(_settings.GameDetectionEnabled, profiles)) return;
        var snapshot = await Task.Run(() => GameDetectionScanner.ScanIfEnabled(true, profiles, _gameEnumerator, token), token);
        token.ThrowIfCancellationRequested();
        if (!_settings.GameDetectionEnabled || _isPreparingToQuit || (CaptureActive && !_backgroundReplayOwned) || _isRecordingDiagnosticRunning ||
            DiagnosticReservationAllowed?.Invoke() == false || !profiles.SequenceEqual(_settings.ApprovedGameProfiles)) return;

        // Absence from window enumeration is NOT exit. Probe each previously
        // approved instance; inaccessible processes remain suppressed.
        var old = _gameSuppressed.ToArray();
        var observed = old.Concat(_automaticReplaySession.FailedGames)
            .GroupBy(process => process.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
        var exited = await Task.Run(() => observed.Where(process => _gameEnumerator.Probe(process) == GameProcessPresence.Exited).ToArray(), token);
        _automaticReplaySession.RemoveExitedGames(exited);
        if (exited.Any(process => old.Any(paused => paused.Matches(process))))
        {
            var retained = old.Where(process => !exited.Any(gone => gone.Matches(process))).ToArray();
            await Task.Run(() => _gameSuppressionStore!.Save(retained), token);
            _gameSuppressed.Clear(); _gameSuppressed.AddRange(retained);
        }
        var decision = GameDetectionPolicy.Evaluate(true, profiles, snapshot, checked((uint)Environment.ProcessId),
            Environment.ProcessPath ?? "", _gameSuppressed);
        _lastGameDetectionReason = decision.Reason;
        _gameReplayWaitingHint = decision.Reason switch
        {
            GameDetectionReason.Suppressed => "Replay paused for game",
            GameDetectionReason.Ambiguous => "Choose a game source",
            GameDetectionReason.Ineligible => "Waiting for game window",
            GameDetectionReason.Unavailable => "Game detection unavailable",
            GameDetectionReason.NoProfiles => "Add a game",
            GameDetectionReason.InvalidProfiles => "Review game settings",
            _ => "Waiting for game"
        };
        PublishGameAutomation(decision.StatusText);
        if (decision.Reason != GameDetectionReason.Eligible || decision.Window == null || decision.Profile == null) return;
        if (!_automaticReplaySession.CanTryGame(decision.Window.Process))
        {
            _gameReplayWaitingHint = "Start replay to retry";
            PublishGameAutomation("Game replay couldn't start. Start replay to retry.");
            return;
        }
        var candidate = await Task.Run(() => _gameEnumerator.Revalidate(decision.Window), token);
        token.ThrowIfCancellationRequested();
        if (candidate?.Eligible != true || candidate.Hwnd != decision.Window.Hwnd ||
            !candidate.Process.Matches(decision.Window.Process) || !_settings.GameDetectionEnabled ||
            (CaptureActive && !_backgroundReplayOwned) || _isPreparingToQuit ||
            DiagnosticReservationAllowed?.Invoke() == false || !profiles.SequenceEqual(_settings.ApprovedGameProfiles)) return;
        GameCapturePlan plan;
        try { plan = GameCapturePlan.Create(_settings, decision.Profile, candidate); }
        catch (InvalidOperationException error)
        {
            _gameReplayWaitingHint = "Review game audio";
            PublishGameAutomation("Automatic replay is waiting. " + error.Message);
            return; // No helper, fallback or new process-audio consent is created.
        }
        // A successful/failed start is not a manual pause. Durable suppression
        // is written only by PauseAutomaticReplayAsync after a real user Stop.
        if (_backgroundReplayOwned)
        {
            await StopCaptureManagedAsync();
            _backgroundReplayOwned = false;
            _automaticReplaySession.ResetBackgroundAttempt();
        }
        token.ThrowIfCancellationRequested();
        _automaticGame = candidate; _automaticGameProfile = decision.Profile;
        PublishGameAutomation("Starting replay · " + decision.Profile.Name);
        var settings = plan.Settings;
        _importCancellation?.Cancel(); _thumbnailCancellation?.Cancel();
        _captureEpoch++;
        _healthSession = _health.BeginSession(new RecordingSource("Window", decision.Profile.Name + " (automatic)", settings.WindowExecutable));
        SetCaptureProfile(null); CaptureStatus = "Starting";
        PublishHealth();
        bool started = false;
        using var interrupt = token.Register(() => _ = Task.Run(() => _captureHost.Dispose()));
        try
        {
            var capabilities = await _captureHost.GetCapabilitiesAsync();
            token.ThrowIfCancellationRequested();
            if (!capabilities.ExactWindowBinding) throw new InvalidOperationException("The helper cannot bind an approved game exactly.");
            started = await _captureHost.StartCaptureAsync(settings, plan.SourceOptions);
            token.ThrowIfCancellationRequested();
            if (!started) throw new InvalidOperationException(_captureHost.LastStartError);
        }
        catch (Exception error)
        {
            await StopCaptureManagedAsync(); ClearAutomaticGame();
            if (error is not OperationCanceledException)
            {
                _automaticReplaySession.RecordGameFailure(candidate.Process);
                _gameReplayWaitingHint = "Start replay to retry";
                RecordCaptureFailure("automatic-start-failed", "Game replay couldn't start: " + error.Message);
            }
            PublishGameAutomation("Game replay couldn't start. Start replay to retry.");
        }
    }

    private async Task ReserveManualGameSessionsAsync(GameProcessIdentity? active = null)
    {
        if (!GameDetectionPolicy.ShouldScan(_settings.GameDetectionEnabled, _settings.ApprovedGameProfiles)) return;
        if (_gameStateUnavailable || _gameSuppressionStore == null)
            throw new InvalidOperationException("Turn off automatic game replay in Games before starting manually; its pause state is unavailable.");
        var profiles = _settings.ApprovedGameProfiles.ToArray();
        var snapshot = await Task.Run(() => _gameEnumerator.Scan(CancellationToken.None));
        if (snapshot.Status != GameScanStatus.Complete) throw new IOException("Approved game sessions could not be verified safely.");
        var identities = snapshot.Windows.Where(window => window.IdentityComplete && window.Process.IsComplete &&
            profiles.Any(profile => profile.Enabled && string.Equals(profile.ExecutablePath, window.Process.ExecutablePath,
                StringComparison.OrdinalIgnoreCase))).Select(window => window.Process);
        IEnumerable<GameProcessIdentity> activeProcess = active is { } current ? [current] : [];
        var reserved = _gameSuppressed.Concat(identities).Concat(activeProcess)
            .GroupBy(identity => identity.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        if (reserved.Length == _gameSuppressed.Count) return;
        await Task.Run(() => _gameSuppressionStore.Save(reserved));
        _gameSuppressed.Clear(); _gameSuppressed.AddRange(reserved);
    }

    private void ClearAutomaticGame()
    {
        _automaticGame = null; _automaticGameProfile = null;
        PublishGameAutomation();
    }

    private async Task QuiesceGameAutomationAsync()
    {
        _gameOperationCancellation?.Cancel();
        if (_gameOperationTask is { } work) await work;
    }

    private void DisposeGameAutomation()
    {
        _gameAutomationDisposed = true;
        _gameTimer?.Stop();
        _gameOperationCancellation?.Cancel();
    }
}
