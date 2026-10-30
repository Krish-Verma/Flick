namespace LocalClip.Core.GameDetection;

/// <summary>Per-application retry/pause intent. Failed starts never become durable user pauses.</summary>
public sealed class AutomaticReplaySession
{
    private readonly Dictionary<string, GameProcessIdentity> _failedGames = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastBackgroundAttempt;
    public bool BackgroundEnabled { get; private set; }
    public bool BackgroundPaused { get; private set; }
    public bool ResourceLimitSuspended { get; private set; }
    public IReadOnlyList<GameProcessIdentity> FailedGames => _failedGames.Values.ToArray();

    public void ConfigureBackground(bool enabled)
    {
        if (BackgroundEnabled != enabled)
        {
            BackgroundPaused = false;
            _lastBackgroundAttempt = null;
        }
        BackgroundEnabled = enabled;
    }

    public void PauseBackground() => BackgroundPaused = true;
    public void ResumeBackground() => BackgroundPaused = false;

    public bool CanTryBackground(string? sourceKey, bool captureActive, bool blocked, bool requiresSettingsReview = false) =>
        BackgroundEnabled && !BackgroundPaused && !ResourceLimitSuspended && !captureActive && !blocked && !requiresSettingsReview &&
        !string.IsNullOrWhiteSpace(sourceKey) &&
        !string.Equals(sourceKey, _lastBackgroundAttempt, StringComparison.Ordinal);

    public void RecordBackgroundAttempt(string sourceKey) => _lastBackgroundAttempt = sourceKey;
    public void ResetBackgroundAttempt() => _lastBackgroundAttempt = null;
    public void RetryFailedGames() => _failedGames.Clear();

    public bool CanTryGame(GameProcessIdentity process) => !ResourceLimitSuspended && process.IsComplete &&
        _failedGames.Count < GameSuppressionStore.MaximumEntries && !_failedGames.ContainsKey(process.Key);
    public void SuspendForResourceLimit() => ResourceLimitSuspended = true;
    public void AllowResourceRetry() => ResourceLimitSuspended = false;

    public void RecordGameFailure(GameProcessIdentity process)
    {
        if (process.IsComplete && _failedGames.Count < GameSuppressionStore.MaximumEntries)
            _failedGames.TryAdd(process.Key, process);
    }

    public void RemoveExitedGames(IEnumerable<GameProcessIdentity> exited)
    {
        foreach (var identity in exited) _failedGames.Remove(identity.Key);
    }

    public static IReadOnlyList<GameProcessIdentity> PausesAfterExplicitResume(
        IReadOnlyList<GameProcessIdentity> paused, IReadOnlyList<ApprovedGameProfile> profiles, GameWindowSnapshot snapshot)
    {
        if (snapshot.Status != GameScanStatus.Complete || !GameDetectionPolicy.ValidateProfiles(profiles, out _))
            throw new InvalidOperationException("Running game identities could not be verified.");
        var resumed = snapshot.Windows.Where(window => window.Eligible && profiles.Any(profile => profile.Enabled &&
            string.Equals(profile.ExecutablePath.Replace('/', '\\'), window.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            .Select(window => window.Process).ToArray();
        return paused.Where(identity => !resumed.Any(current => current.Matches(identity))).ToArray();
    }

    public static bool HasBlockingGamePause(bool gameDetectionEnabled, IReadOnlyList<ApprovedGameProfile> profiles,
        IReadOnlyList<GameProcessIdentity> paused) => gameDetectionEnabled &&
        paused.Any(identity => profiles.Any(profile => profile.Enabled &&
            string.Equals(profile.ExecutablePath.Replace('/', '\\'), identity.ExecutablePath, StringComparison.OrdinalIgnoreCase)));
}
