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
        PublishDiagnosticTest();
    }

    public async Task ApplyGameDetectionPreferencesAsync()
    {
        string preferences = JsonSerializer.Serialize(new { _settings.GameDetectionEnabled, _settings.ApprovedGameProfiles });
        if (_appliedGamePreferences == preferences && !_gameStateUnavailable) return;
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
            PublishGameAutomation(new GameDetectionDecision(_settings.GameDetectionEnabled
                ? GameDetectionReason.NoProfiles : GameDetectionReason.Disabled).StatusText);
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
            foreach (var process in saved)
                if (!_gameSuppressed.Any(current => current.Matches(process))) _gameSuppressed.Add(process);
            _gameStateUnavailable = false;
        }
        catch (Exception)
        {
            _gameStateUnavailable = true;
            PublishGameAutomation("Automatic game replay is paused because its saved session state could not be read safely.");
            return;
        }
        _gameTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _gameTimer.Tick -= GameTimerTick;
        _gameTimer.Tick += GameTimerTick;
        _gameTimer.Start();
        PublishGameAutomation("Waiting for an approved game. Manual replay preferences are unchanged.");
    }

    private async void GameTimerTick(object? sender, EventArgs e)
    {
        if (_gameOperationTask != null || _gameAutomationDisposed || _isPreparingToQuit || _gameStateUnavailable ||
            _isRecordingDiagnosticRunning || _diagnosticCleanupUnconfirmed || _refreshingTargets || _pollInProgress || _manualStopOperation != null ||
            DiagnosticReservationAllowed?.Invoke() == false) return;
        if (_automaticGame == null && (_manualReplayReserved || CaptureActive || _stopCaptureTask != null || IsReplaySavePending ||
            IsImporting || IsExporting || IsBackgroundWorkActive)) return;
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
            var current = GameDetectionPolicy.Evaluate(configured, _settings.ApprovedGameProfiles, activeSnapshot,
                checked((uint)Environment.ProcessId), Environment.ProcessPath ?? "");
            bool sameChoice = current.Reason == GameDetectionReason.Eligible && current.Window != null &&
                current.Window.Process.Matches(active.Process) && current.Window.Hwnd == active.Hwnd &&
                current.Profile?.Id == _automaticGameProfile?.Id;
            if (!configured || !sameChoice || CaptureStatus is "Faulted" or "Off")
            {
                await StopCaptureManagedAsync(); ClearAutomaticGame();
                PublishGameAutomation("Automatic replay stopped. This game session stays paused; start manually or restart the game.");
            }
            return;
        }
        var profiles = _settings.ApprovedGameProfiles.ToArray();
        if (!GameDetectionPolicy.ShouldScan(_settings.GameDetectionEnabled, profiles)) return;
        var snapshot = await Task.Run(() => GameDetectionScanner.ScanIfEnabled(true, profiles, _gameEnumerator, token), token);
        token.ThrowIfCancellationRequested();
        if (!_settings.GameDetectionEnabled || _isPreparingToQuit || CaptureActive || _isRecordingDiagnosticRunning ||
            DiagnosticReservationAllowed?.Invoke() == false || !profiles.SequenceEqual(_settings.ApprovedGameProfiles)) return;

        // Absence from window enumeration is NOT exit. Probe each previously
        // approved instance; inaccessible processes remain suppressed.
        var old = _gameSuppressed.ToArray();
        var exited = await Task.Run(() => old.Where(process => _gameEnumerator.Probe(process) == GameProcessPresence.Exited).ToArray(), token);
        if (exited.Length != 0)
        {
            var retained = old.Where(process => !exited.Any(gone => gone.Matches(process))).ToArray();
            await Task.Run(() => _gameSuppressionStore!.Save(retained), token);
            _gameSuppressed.Clear(); _gameSuppressed.AddRange(retained);
        }
        var decision = GameDetectionPolicy.Evaluate(true, profiles, snapshot, checked((uint)Environment.ProcessId),
            Environment.ProcessPath ?? "", _gameSuppressed);
        PublishGameAutomation(decision.StatusText);
        if (decision.Reason != GameDetectionReason.Eligible || decision.Window == null || decision.Profile == null) return;
        var candidate = await Task.Run(() => _gameEnumerator.Revalidate(decision.Window), token);
        token.ThrowIfCancellationRequested();
        if (candidate?.Eligible != true || !_settings.GameDetectionEnabled || CaptureActive || _isPreparingToQuit ||
            DiagnosticReservationAllowed?.Invoke() == false || !profiles.SequenceEqual(_settings.ApprovedGameProfiles)) return;
        GameCapturePlan plan;
        try { plan = GameCapturePlan.Create(_settings, decision.Profile, candidate); }
        catch (InvalidOperationException error)
        {
            PublishGameAutomation("Automatic replay is waiting. " + error.Message);
            return; // No helper, fallback or new process-audio consent is created.
        }
        // Persist the reservation before recording. A crash/restart or failed
        // startup cannot unexpectedly retry the same still-running process.
        var reserved = _gameSuppressed.Append(candidate.Process).ToArray();
        await Task.Run(() => _gameSuppressionStore!.Save(reserved), token);
        _gameSuppressed.Add(candidate.Process);
        token.ThrowIfCancellationRequested();
        _automaticGame = candidate; _automaticGameProfile = decision.Profile;
        PublishGameAutomation("Automatic replay · " + decision.Profile.Name + ". Stop Replay pauses this game session.");
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
                RecordCaptureFailure("automatic-start-failed", "Automatic game replay could not start: " + error.Message);
            PublishGameAutomation("Automatic replay is paused for this game session. Review recording health or start manually.");
        }
    }

    private async Task ReserveManualGameSessionsAsync()
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
        var reserved = _gameSuppressed.Concat(identities).GroupBy(identity => identity.Key, StringComparer.OrdinalIgnoreCase)
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
