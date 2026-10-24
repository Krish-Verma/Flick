using System.Text.Json;
using LocalClip.Core.GameDetection;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;

namespace LocalClip.Core.Tests;

public sealed class GameDetectionTests
{
    private static ApprovedGameProfile Profile(string path = @"C:\Games\One\game.exe") =>
        new() { Id = Guid.NewGuid(), Enabled = true, Name = "Approved Game", ExecutablePath = path };
    private static GameWindowObservation Window(string path = @"C:\Games\One\game.exe", uint pid = 20, ulong birth = 100, ulong hwnd = 50) =>
        new(new(path, pid, birth), hwnd, "Game", "GameWindow", true, false, false, false, 1280, 720);
    private static GameDetectionDecision Decide(ApprovedGameProfile[] profiles, params GameWindowObservation[] windows) =>
        GameDetectionPolicy.Evaluate(true, profiles, new(GameScanStatus.Complete, windows), 999, @"C:\Flick\Flick.exe");

    [Fact]
    public void DisabledEmptyAndInvalidConfigurationsNeverCallNativeEnumerator()
    {
        var spy = new SpyEnumerator();
        GameDetectionScanner.ScanIfEnabled(false, [Profile()], spy, default);
        GameDetectionScanner.ScanIfEnabled(true, [], spy, default);
        GameDetectionScanner.ScanIfEnabled(true, [Profile("game.exe")], spy, default);
        GameDetectionScanner.ScanIfEnabled(true, [Profile() with { Enabled = false }], spy, default);
        Assert.Equal(0, spy.Scans);
        GameDetectionScanner.ScanIfEnabled(true, [Profile()], spy, default);
        Assert.Equal(1, spy.Scans);
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData(@"C:game.exe")]
    [InlineData(@"\\server\share\game.exe")]
    [InlineData(@"C:\Games\..\game.exe")]
    [InlineData(@"C:\Games\*.exe")]
    [InlineData(@"C:\Games\game.exe:stream")]
    public void UnobservedOrAmbiguousPathFormsAreRejected(string path) =>
        Assert.False(GameDetectionPolicy.ValidateProfiles([Profile(path)], out _));

    [Fact]
    public void FullPathApprovalCannotMatchSameBasenameInAnotherDirectory()
    {
        Assert.Equal(GameDetectionReason.NoMatch, Decide([Profile()], Window(@"C:\Other\game.exe")).Reason);
        Assert.Equal(GameDetectionReason.Eligible, Decide([Profile()], Window(@"c:\games\ONE\GAME.EXE")).Reason);
        Assert.Equal(GameDetectionReason.InvalidProfiles, Decide([Profile(), Profile(@"c:\GAMES\one\GAME.exe")], Window()).Reason);
    }

    [Fact]
    public void MultipleGamesInstancesOrWindowsNeverChooseFirstOrForeground()
    {
        Assert.Equal(GameDetectionReason.Ambiguous, Decide([Profile()], Window(), Window(hwnd: 51)).Reason);
        Assert.Equal(GameDetectionReason.Ambiguous, Decide([Profile()], Window(), Window(pid: 21, hwnd: 51)).Reason);
        Assert.Equal(GameDetectionReason.Ambiguous, Decide([Profile()], Window(), Window(pid: 21, hwnd: 51) with { Minimized = true }).Reason);
        Assert.Equal(GameDetectionReason.Ambiguous, Decide([Profile(), Profile(@"D:\Games\two.exe")], Window(), Window(@"D:\Games\two.exe", 22, 105, 52)).Reason);
    }

    [Fact]
    public void MinimizedCloakedElevatedAndUnknownIdentityAreIneligible()
    {
        foreach (var window in new[] { Window() with { Minimized = true }, Window() with { Visible = false },
            Window() with { Cloaked = true }, Window() with { Elevated = true }, Window() with { ToolWindow = true } })
            Assert.Equal(GameDetectionReason.Ineligible, Decide([Profile()], window).Reason);
        Assert.Equal(GameDetectionReason.NoMatch, Decide([Profile()], Window() with { IdentityComplete = false }).Reason);
        Assert.Equal(GameDetectionReason.NoMatch, Decide([Profile()], Window(birth: 0)).Reason);
    }

    [Fact]
    public void AltTabAndTitleChangesDoNotBroadenOrRevokeExecutableApproval()
    {
        Assert.Equal(GameDetectionReason.Eligible, Decide([Profile()], Window() with { Title = "Game — new level" }).Reason);
        Assert.Equal(GameDetectionReason.Ineligible, Decide([Profile()], Window() with { WindowClass = "WorkerW" }).Reason);
        Assert.Equal(GameDetectionReason.NoMatch, GameDetectionPolicy.Evaluate(true, [Profile()],
            new(GameScanStatus.Complete, [Window()]), 20, @"C:\Flick\Flick.exe").Reason);
    }

    [Theory]
    [InlineData(GameScanStatus.Unavailable)]
    [InlineData(GameScanStatus.Truncated)]
    public void IncompleteScanCannotProduceCaptureDecision(GameScanStatus status) =>
        Assert.Equal(GameDetectionReason.Unavailable, GameDetectionPolicy.Evaluate(true, [Profile()],
            new(status, [Window()]), 999, "").Reason);

    [Fact]
    public void ManualSuppressionIsProcessInstanceScopedAndSurvivesWindowChanges()
    {
        var suppressed = Window().Process;
        var restored = Window(hwnd: 100) with { Title = "New window in same process" };
        Assert.Equal(GameDetectionReason.Suppressed, GameDetectionPolicy.Evaluate(true, [Profile()],
            new(GameScanStatus.Complete, [restored]), 999, "", [suppressed]).Reason);
        Assert.Equal(GameDetectionReason.Eligible, GameDetectionPolicy.Evaluate(true, [Profile()],
            new(GameScanStatus.Complete, [Window(birth: 101)]), 999, "", [suppressed]).Reason);
    }

    [Fact]
    public void SuppressionSurvivesApplicationRestartAndCorruptStateIsPreserved()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "flick-game-state-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "state.json");
        try
        {
            var store = new GameSuppressionStore(path);
            store.Save([Window().Process]);
            var reloaded = new GameSuppressionStore(path).Load();
            Assert.True(Assert.Single(reloaded).Matches(Window().Process));
            Assert.Equal(GameDetectionReason.Suppressed, GameDetectionPolicy.Evaluate(true, [Profile()],
                new(GameScanStatus.Complete, [Window()]), 999, "", reloaded).Reason);
            const string corrupt = "{bad state";
            File.WriteAllText(path, corrupt);
            Assert.Throws<JsonException>(() => store.Load());
            Assert.Equal(corrupt, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(5)]
    public void LegacyAndUnknownSchemasNeverPromoteSavedTargetOrNewFieldsToApproval(int schema)
    {
        var settings = AppSettings.CreateDefault();
        settings.SchemaVersion = schema; settings.GameDetectionEnabled = true;
        settings.ApprovedGameProfiles = [Profile()]; settings.WindowExecutable = @"C:\Games\One\game.exe";
        string raw = JsonSerializer.Serialize(settings), path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, raw);
            var loaded = AppSettings.LoadExistingFile(path);
            Assert.False(loaded.GameDetectionEnabled); Assert.Empty(loaded.ApprovedGameProfiles);
            Assert.True(loaded.RequiresExplicitSettingsSave); Assert.Equal(raw, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ValidExplicitProfilesRoundTripWithoutChangingManualSourceOrMicrophone()
    {
        var settings = AppSettings.CreateDefault(); settings.GameDetectionEnabled = true;
        settings.ApprovedGameProfiles = [Profile()]; settings.EnableMicrophone = true;
        settings.TargetType = "Monitor"; settings.MonitorIndex = 2;
        string path = Path.GetTempFileName();
        try
        {
            settings.SaveToFile(path);
            var restored = AppSettings.LoadExistingFile(path);
            Assert.True(restored.GameDetectionEnabled); Assert.False(Assert.Single(restored.ApprovedGameProfiles).AllowMicrophone);
            Assert.True(restored.EnableMicrophone); Assert.Equal("Monitor", restored.TargetType); Assert.Equal(2, restored.MonitorIndex);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FullIdentityIsTransientOnWireAndOldHelpersCannotClaimExactWindowBinding()
    {
        var settings = AppSettings.CreateDefault(); settings.TargetType = "Window"; settings.WindowHwnd = 50;
        var options = new CaptureStartOptions(ExpectedWindow: new(50, 20, "Game", "game.exe", @"C:\Games\One\game.exe", 100));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(CaptureHostClient.CreateStartParameters(settings, options)));
        Assert.Equal(100UL, json.RootElement.GetProperty("expected_window_creation_time").GetUInt64());
        Assert.Equal(@"C:\Games\One\game.exe", json.RootElement.GetProperty("expected_window_path").GetString());
        Assert.False(new CaptureHostCapabilities(2, true, true).ExactWindowBinding);
        Assert.Throws<ArgumentException>(() => CaptureHostClient.CreateStartParameters(settings,
            new(ExpectedWindow: options.ExpectedWindow! with { ProcessCreationFileTime = 0 })));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void AutomaticPlanUsesDeepCopyAndRequiresBothMicrophoneConsents(bool global, bool perGame, bool expected)
    {
        var manual = AppSettings.CreateDefault(); manual.EnableMicrophone = global;
        manual.TargetType = "Monitor"; manual.MonitorIndex = 3;
        string before = JsonSerializer.Serialize(manual);
        var profile = Profile() with { AllowMicrophone = perGame, QualityPreset = "Light", ReplayDurationSeconds = 60 };
        var plan = GameCapturePlan.Create(manual, profile, Window());
        Assert.Equal(expected, plan.Settings.EnableMicrophone);
        Assert.Equal("Window", plan.Settings.TargetType); Assert.Equal(50UL, plan.Settings.WindowHwnd);
        Assert.Equal(60, plan.Settings.ReplayDurationSeconds); Assert.Equal(1280, plan.Settings.VideoWidth);
        Assert.Equal(before, JsonSerializer.Serialize(manual));
        Assert.Equal(Window().Process.CreationFileTime, plan.SourceOptions.ExpectedWindow!.ProcessCreationFileTime);
        Assert.Equal(profile.ExecutablePath, plan.SourceOptions.ExpectedWindow.ExecutablePath);
        Assert.NotSame(manual.ApprovedGameProfiles, plan.Settings.ApprovedGameProfiles);
    }

    private sealed class SpyEnumerator : IGameWindowEnumerator
    {
        public int Scans;
        public GameWindowSnapshot Scan(CancellationToken token) { Scans++; return new(GameScanStatus.Complete, []); }
        public GameProcessPresence Probe(GameProcessIdentity process) => GameProcessPresence.Unknown;
        public GameWindowObservation? Revalidate(GameWindowObservation window) => window;
    }
}
