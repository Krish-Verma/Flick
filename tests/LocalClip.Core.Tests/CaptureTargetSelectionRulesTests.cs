using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class CaptureTargetSelectionRulesTests
{
    [Fact]
    public void MissingSavedWindowNeverFallsBackToMonitor()
    {
        var saved = AppSettings.CreateDefault();
        saved.TargetType = "Window";
        saved.WindowHwnd = 123456;
        saved.WindowTitle = "My Game";
        saved.WindowExecutable = @"C:\\Games\\game.exe";
        Assert.False(CaptureTargetSelectionRules.MatchesSavedPreference(
            saved, "Monitor", 0, "", ""));
        Assert.False(CaptureTargetSelectionRules.MatchesSavedPreference(
            saved, "Window", 0, "Unrelated App", @"C:\\Games\\game.exe"));
        Assert.False(CaptureTargetSelectionRules.MatchesSavedPreference(
            saved, "Window", 0, "My Game", @"C:\\Other\\game.exe"));
    }

    [Fact]
    public void WindowMatchingRequiresStableTitleAndExecutableNotHandle()
    {
        var saved = AppSettings.CreateDefault();
        saved.TargetType = "Window";
        saved.WindowHwnd = 999;
        saved.WindowTitle = "My Game";
        saved.WindowExecutable = @"C:\\Games\\game.exe";
        Assert.True(CaptureTargetSelectionRules.MatchesSavedPreference(
            saved, "Window", 0, "My Game", @"c:\\games\\GAME.EXE"));
        saved.WindowExecutable = "";
        Assert.False(CaptureTargetSelectionRules.MatchesSavedPreference(
            saved, "Window", 0, "My Game", @"C:\\Games\\game.exe"));
    }

    [Theory]
    [InlineData("Monitor", 1, true)]
    [InlineData("Monitor", 2, false)]
    [InlineData("Window", 1, false)]
    public void MonitorRestorationNeedsSameTypeAndIndex(
        string candidateType, int candidateIndex, bool expected)
    {
        var saved = AppSettings.CreateDefault();
        saved.TargetType = "Monitor";
        saved.MonitorIndex = 1;
        Assert.Equal(expected, CaptureTargetSelectionRules.MatchesSavedPreference(
            saved, candidateType, candidateIndex, "", ""));
    }
}
