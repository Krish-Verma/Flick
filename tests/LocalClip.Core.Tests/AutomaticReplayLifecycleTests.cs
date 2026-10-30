using System.Text.Json;
using LocalClip.Core.GameDetection;

namespace LocalClip.Core.Tests;

public sealed class AutomaticReplayLifecycleTests
{
    private static readonly ApprovedGameProfile Profile = new()
    {
        Id = Guid.Parse("0f69a47e-393a-4333-bb03-971596d92a9c"), Enabled = true,
        Name = "Game", ExecutablePath = @"C:\Games\Game.exe"
    };
    private static GameWindowObservation Window(uint pid = 20, ulong birth = 100, ulong hwnd = 50) =>
        new(new(Profile.ExecutablePath, pid, birth), hwnd, "Game", "GameWindow", true, false, false, false, 1280, 720);
    private static GameWindowSnapshot Snapshot(params GameWindowObservation[] windows) => new(GameScanStatus.Complete, windows);

    [Fact]
    public void SecondaryWindowCanResolveToExactForegroundWithinSameApprovedProcess()
    {
        var game = Window() with { IsForeground = true };
        var launcher = Window(hwnd: 51);
        var decision = GameDetectionPolicy.Evaluate(true, [Profile], Snapshot(launcher, game), 999, "");
        Assert.Equal(GameDetectionReason.Eligible, decision.Reason);
        Assert.Equal(game, decision.Window);
        Assert.Equal(GameDetectionReason.Ambiguous, GameDetectionPolicy.Evaluate(true, [Profile],
            Snapshot(game with { IsForeground = false }, launcher), 999, "").Reason);
    }

    [Fact]
    public void ForegroundNeverResolvesTwoSeparateGameProcesses()
    {
        Assert.Equal(GameDetectionReason.Ambiguous, GameDetectionPolicy.Evaluate(true, [Profile],
            Snapshot(Window() with { IsForeground = true }, Window(21, 101, 51)), 999, "").Reason);
    }

    [Fact]
    public void ActiveExactWindowSurvivesFocusChangeOrSecondaryWindowWithoutRebinding()
    {
        var active = Window();
        Assert.True(GameDetectionPolicy.CanContinue(Profile, active,
            Snapshot(active, Window(hwnd: 51) with { IsForeground = true })));
        Assert.False(GameDetectionPolicy.CanContinue(Profile, active, Snapshot(Window(birth: 101))));
        Assert.False(GameDetectionPolicy.CanContinue(Profile, active, Snapshot(Window(hwnd: 51))));
        Assert.False(GameDetectionPolicy.CanContinue(Profile, active, new(GameScanStatus.Truncated, [active])));
    }

    [Fact]
    public void TemporaryMinimizeStopsEligibilityAndRestoreIsEligibleWithoutUserPause()
    {
        var active = Window();
        Assert.False(GameDetectionPolicy.CanContinue(Profile, active, Snapshot(active with { Minimized = true })));
        Assert.True(GameDetectionPolicy.CanContinue(Profile, active, Snapshot(active)));
        Assert.Equal(GameDetectionReason.Eligible, GameDetectionPolicy.Evaluate(true, [Profile], Snapshot(active), 999, "").Reason);
    }

    [Fact]
    public void FailedAttemptDoesNotBecomePauseAcrossApplicationRestartOrNewProcessInstance()
    {
        var session = new AutomaticReplaySession();
        session.RecordGameFailure(Window().Process);
        Assert.False(session.CanTryGame(Window().Process));
        Assert.True(session.CanTryGame(Window(birth: 101).Process));
        Assert.True(new AutomaticReplaySession().CanTryGame(Window().Process));
        session.RetryFailedGames();
        Assert.True(session.CanTryGame(Window().Process));
    }

    [Fact]
    public void ExitPrunesFailedAttemptsWithoutUnpausingAnotherProcess()
    {
        var session = new AutomaticReplaySession();
        session.RecordGameFailure(Window().Process);
        session.RecordGameFailure(Window(21, 101).Process);
        session.RemoveExitedGames([Window().Process]);
        Assert.True(session.CanTryGame(Window().Process));
        Assert.False(session.CanTryGame(Window(21, 101).Process));
    }

    [Fact]
    public void BackgroundRequiresAvailableSourceAndNoActiveOrBusyWork()
    {
        var session = new AutomaticReplaySession();
        Assert.False(session.CanTryBackground("Monitor|0", false, false));
        session.ConfigureBackground(true);
        Assert.False(session.CanTryBackground(null, false, false));
        Assert.False(session.CanTryBackground("Monitor|0", true, false));
        Assert.False(session.CanTryBackground("Monitor|0", false, true));
        Assert.True(session.CanTryBackground("Monitor|0", false, false));
        session.RecordBackgroundAttempt("Monitor|0");
        Assert.False(session.CanTryBackground("Monitor|0", false, false));
        Assert.True(session.CanTryBackground("Monitor|1", false, false));
        session.ResetBackgroundAttempt();
        Assert.True(session.CanTryBackground("Monitor|0", false, false));
    }

    [Fact]
    public void ManualBackgroundStopSurvivesRepeatedApplyAndFailedAttemptRetry()
    {
        var session = new AutomaticReplaySession();
        session.ConfigureBackground(true);
        session.PauseBackground();
        session.ConfigureBackground(true);
        session.ResetBackgroundAttempt();
        session.RetryFailedGames();
        Assert.False(session.CanTryBackground("Monitor|0", false, false));
        session.ConfigureBackground(false);
        session.ConfigureBackground(true);
        Assert.True(session.CanTryBackground("Monitor|0", false, false));
    }

    [Fact]
    public void LegacyStartReservationsDoNotSuppressRestartButVersionTwoUserPauseDoes()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "flick-replay-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "state.json");
        try
        {
            string legacy = JsonSerializer.Serialize(new { SchemaVersion = 1, Suppressed = new[] { Window().Process } });
            File.WriteAllText(path, legacy);
            var store = new GameSuppressionStore(path);
            Assert.Empty(store.Load());
            Assert.Equal(legacy, File.ReadAllText(path));
            store.Save([Window().Process]);
            using var persisted = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(2, persisted.RootElement.GetProperty("SchemaVersion").GetInt32());
            var pauses = new GameSuppressionStore(path).Load();
            Assert.Equal(GameDetectionReason.Suppressed, GameDetectionPolicy.Evaluate(true, [Profile],
                Snapshot(Window()), 999, "", pauses).Reason);
            Assert.Equal(GameDetectionReason.Eligible, GameDetectionPolicy.Evaluate(true, [Profile],
                Snapshot(Window(birth: 101)), 999, "", pauses).Reason);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void MalformedLegacyPauseStateIsStillPreservedAndRefused()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "flick-replay-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "state.json");
        try
        {
            const string bad = "{\"SchemaVersion\":1,\"Suppressed\":[{\"Pid\":0}]}";
            File.WriteAllText(path, bad);
            Assert.Throws<InvalidDataException>(() => new GameSuppressionStore(path).Load());
            Assert.Equal(bad, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExplicitStartClearsCurrentGamePauseAcrossQuitAndReopenButRetainsUnrelatedPauses()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "flick-replay-resume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "state.json");
        try
        {
            var game = Window();
            var unrelated = Window(21, 101).Process;
            var store = new GameSuppressionStore(path);
            store.Save([game.Process, unrelated]);
            var session = new AutomaticReplaySession();
            session.ConfigureBackground(true);
            session.PauseBackground();
            var retained = AutomaticReplaySession.PausesAfterExplicitResume(store.Load(), [Profile], Snapshot(game));
            store.Save(retained);
            session.ResumeBackground();
            Assert.False(session.BackgroundPaused);
            Assert.True(Assert.Single(new GameSuppressionStore(path).Load()).Matches(unrelated));
            Assert.Equal(GameDetectionReason.Eligible, GameDetectionPolicy.Evaluate(true, [Profile], Snapshot(game),
                999, "", new GameSuppressionStore(path).Load()).Reason);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExplicitResumeCannotClearPauseForRecycledMinimizedOrDisabledGame()
    {
        var paused = Window().Process;
        Assert.Single(AutomaticReplaySession.PausesAfterExplicitResume([paused], [Profile], Snapshot(Window(birth: 101))));
        Assert.Single(AutomaticReplaySession.PausesAfterExplicitResume([paused], [Profile], Snapshot(Window() with { Minimized = true })));
        Assert.Single(AutomaticReplaySession.PausesAfterExplicitResume([paused], [Profile with { Enabled = false }], Snapshot(Window())));
        Assert.Single(AutomaticReplaySession.PausesAfterExplicitResume([paused], [Profile], Snapshot()));
    }

    [Fact]
    public void IncompleteResumeSnapshotCannotBroadenSavedPauseIntent()
    {
        foreach (var status in new[] { GameScanStatus.Truncated, GameScanStatus.Unavailable })
            Assert.Throws<InvalidOperationException>(() => AutomaticReplaySession.PausesAfterExplicitResume(
                [Window().Process], [Profile], new(status, [Window()])));
    }

    [Fact]
    public void RecoveredSettingsBlockBackgroundReplayUntilExplicitReview()
    {
        var session = new AutomaticReplaySession();
        session.ConfigureBackground(true);
        Assert.False(session.CanTryBackground("Monitor|0", false, false, requiresSettingsReview: true));
        Assert.True(session.CanTryBackground("Monitor|0", false, false, requiresSettingsReview: false));
    }

    [Fact]
    public void DisabledOrRemovedGameAutomationCannotLeaveBackgroundBlockedByStaleGamePause()
    {
        Assert.True(AutomaticReplaySession.HasBlockingGamePause(true, [Profile], [Window().Process]));
        Assert.False(AutomaticReplaySession.HasBlockingGamePause(false, [Profile], [Window().Process]));
        Assert.False(AutomaticReplaySession.HasBlockingGamePause(true, [Profile with { Enabled = false }], [Window().Process]));
        Assert.False(AutomaticReplaySession.HasBlockingGamePause(true, [], [Window().Process]));
        var session = new AutomaticReplaySession();
        session.ConfigureBackground(true);
        session.PauseBackground();
        Assert.False(session.CanTryBackground("Monitor|0", false,
            AutomaticReplaySession.HasBlockingGamePause(false, [Profile], [Window().Process])));
    }
}
