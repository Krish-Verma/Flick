using System;
using System.IO;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class ShareExportTargetsTests
{
    [Fact]
    public void FreeTierDefaultAndStandardPresetsAreDecimalMegabytes()
    {
        Assert.Equal(20, ShareExportTargets.DefaultMb);
        Assert.Equal(new[] { 10, 20, 50, 100 }, ShareExportTargets.Presets);
        Assert.Equal(20_000_000L, ShareExportTargets.ToBytes(20));
        Assert.Equal(10_000_000L, ShareExportTargets.ToBytes(10));
        Assert.Equal(250_000_000L, ShareExportTargets.ToBytes(250));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(251)]
    public void InvalidCustomCapsAreRejected(int cap)
    {
        Assert.False(ShareExportTargets.IsValid(cap));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShareExportTargets.ToBytes(cap));
    }

    [Fact]
    public void StaleOrMissingFilesNeverBecomeDraggable()
    {
        string original = Path.Combine(Path.GetTempPath(), $"flick-original-{Guid.NewGuid():N}.mkv");
        string file = Path.Combine(Path.GetTempPath(), ShareExportNaming.ForOriginal(original, 20));
        try
        {
            File.WriteAllBytes(file, new byte[4096]);
            var clip = new ClipMetadata
            {
                OriginalFilePath = original,
                DurationSeconds = 30,
                TrimEndSeconds = 30,
                ExportedFilePath = file,
                ExportedFileSizeBytes = 4096,
                IsExportReady = true
            };
            clip.LastExportFingerprint = ExportFingerprint.Compute(
                clip.OriginalFilePath, 0, 30, clip.AudioTracks, 20);
            Assert.True(ReadyShareFile.TryGet(clip, 20, out var result));
            Assert.Equal(file, result);
            Assert.False(ReadyShareFile.TryGet(clip, 10, out _));
            clip.TrimStartSeconds = .25;
            Assert.False(ReadyShareFile.TryGet(clip, 20, out _));
            clip.TrimStartSeconds = 0;
            clip.ExportedFileSizeBytes = 4000; // Wrong metadata cannot pass.
            Assert.False(ReadyShareFile.TryGet(clip, 20, out _));
            clip.ExportedFileSizeBytes = 4096;
            File.Delete(file);
            Assert.False(ReadyShareFile.TryGet(clip, 20, out _));
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public void SharePathMustBeMP4RatherThanAnOriginalOrOtherFormat()
    {
        var clip = new ClipMetadata
        {
            OriginalFilePath = "original.mkv", DurationSeconds = 30,
            IsExportReady = true, ExportedFilePath = "original.mkv",
            LastExportFingerprint = "wrong"
        };
        Assert.False(ReadyShareFile.TryGet(clip, 20, out _));
    }
}
