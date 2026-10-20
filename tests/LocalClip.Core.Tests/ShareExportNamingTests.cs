using System;
using System.IO;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class ShareExportNamingTests
{
    [Fact]
    public void SeparateSourcesWithSameBasenameDoNotOverwriteEachOther()
    {
        string first = ShareExportNaming.ForOriginal(@"C:\Videos\SteelSeries\clip.mkv", 20);
        string second = ShareExportNaming.ForOriginal(@"D:\Capture\OBS\clip.mkv", 20);
        Assert.NotEqual(first, second);
        Assert.EndsWith(".mp4", first, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_Discord_20MB_", first);
    }

    [Fact]
    public void SameSourceAndSizeAreDeterministic()
    {
        string original = @"C:\Videos\clip.mkv";
        Assert.Equal(ShareExportNaming.ForOriginal(original, 20),
                     ShareExportNaming.ForOriginal(original, 20));
        Assert.NotEqual(ShareExportNaming.ForOriginal(original, 10),
                        ShareExportNaming.ForOriginal(original, 20));
    }

    [Fact]
    public void ExportsCannotUseInvalidSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShareExportNaming.ForOriginal(@"C:\Videos\clip.mkv", 251));
    }
}
