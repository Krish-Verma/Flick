using System;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public class WorkspaceLayoutRulesTests
{
    [Theory]
    [InlineData(720, WorkspaceLayoutMode.Compact)]
    [InlineData(960, WorkspaceLayoutMode.Compact)]
    [InlineData(1000, WorkspaceLayoutMode.Compact)]
    [InlineData(1119.9, WorkspaceLayoutMode.Compact)]
    [InlineData(1120, WorkspaceLayoutMode.Split)]
    [InlineData(1240, WorkspaceLayoutMode.Split)]
    [InlineData(1920, WorkspaceLayoutMode.Split)]
    [InlineData(double.NaN, WorkspaceLayoutMode.Compact)]
    public void WorkspaceModeFollowsLogicalSize(double width, WorkspaceLayoutMode mode) =>
        Assert.Equal(mode, WorkspaceLayoutRules.Classify(width));

    [Fact]
    public void LogicalMinimumFitsStandard1080pAtDoubleScale()
    {
        Assert.InRange(WorkspaceLayoutRules.MinimumWindowWidthDips, 600, 960);
        Assert.InRange(WorkspaceLayoutRules.MinimumWindowHeightDips, 400, 540);
    }

    [Fact]
    public void CompactActionPreservesRealSaveHotkeyMeaning()
    {
        Assert.Equal("Save Clip", WorkspaceLayoutRules.SaveActionLabel(120, true));
        Assert.Equal("Save Last 30 Seconds", WorkspaceLayoutRules.SaveActionLabel(30, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkspaceLayoutRules.SaveActionLabel(0, true));
    }
}
