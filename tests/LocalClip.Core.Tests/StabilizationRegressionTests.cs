using System;
using System.Collections.Generic;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public class StabilizationRegressionTests
{
    [Fact]
    public void CombinedPreviewTrackMustNotBeRemixedWithDedicatedSources()
    {
        var tracks = new List<AudioTrackInfo>
        {
            new() { TrackIndex = 0, Name = "Combined Mix", IsMuted = true },
            new() { TrackIndex = 1, Name = "System Audio", IsMuted = false },
            new() { TrackIndex = 2, Name = "Microphone", IsMuted = false }
        };
        var filter = ClipExporter.BuildAudioMixFilter(tracks);
        Assert.DoesNotContain("[0:a:0]", filter);
        Assert.Contains("[0:a:1]", filter);
        Assert.Contains("[0:a:2]", filter);
        Assert.Contains("amix=inputs=2", filter);
    }

    [Fact]
    public void AllMutedTracksProduceSilentStereoFilter()
    {
        var filter = ClipExporter.BuildAudioMixFilter(new List<AudioTrackInfo>
        {
            new() { TrackIndex = 0, IsMuted = true },
            new() { TrackIndex = 1, Gain = 0.0 }
        });
        Assert.Equal("anullsrc=r=48000:cl=stereo[outa]", filter);
        // Actual finite-duration FFmpeg behavior must be verified on hardware.
    }

    [Fact]
    public void AChangedAudioMuteInvalidatesShareExportFingerprint()
    {
        var tracks = new List<AudioTrackInfo> { new() { TrackIndex = 1, IsMuted = false, Gain = 1.0 } };
        var original = ExportFingerprint.Compute("example.mkv", 1.0, 20.0, tracks, 20);
        tracks[0].IsMuted = true;
        var muted = ExportFingerprint.Compute("example.mkv", 1.0, 20.0, tracks, 20);
        Assert.NotEqual(original, muted);
        tracks[0].IsMuted = false;
        tracks[0].Gain = 0.5;
        Assert.NotEqual(original, ExportFingerprint.Compute("example.mkv", 1.0, 20.0, tracks, 20));
    }

    [Fact]
    public void FileCapUsesDecimalBytesNotMebibytes()
    {
        Assert.Equal(20_000_000L, BitrateBudgetCalculator.TargetMbToBytes(20));
        Assert.NotEqual(20 * 1024 * 1024, BitrateBudgetCalculator.TargetMbToBytes(20));
    }
}
