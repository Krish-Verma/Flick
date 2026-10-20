using System;
using System.ComponentModel;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public class Slice4EditingTests
{
    [Fact]
    public void AudioMixChangesNotifyImmediatelyAndClampGain()
    {
        var track = new AudioTrackInfo { Name = "System Audio" };
        int gains = 0, mutes = 0;
        track.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AudioTrackInfo.Gain)) gains++;
            if (e.PropertyName == nameof(AudioTrackInfo.IsMuted)) mutes++;
        };
        track.Gain = 1.5;
        track.Gain = 1.5;
        track.Gain = 4.0;
        track.IsMuted = true;
        track.IsMuted = true;
        Assert.Equal(2, gains);
        Assert.Equal(1, mutes);
        Assert.Equal(2d, track.Gain);
        Assert.True(track.IsMuted);
    }

    [Fact]
    public void NonFiniteGainCannotPoisonExporterMath()
    {
        var track = new AudioTrackInfo();
        track.Gain = double.NaN;
        Assert.Equal(1d, track.Gain);
        track.Gain = double.PositiveInfinity;
        Assert.Equal(1d, track.Gain);
        track.Gain = -99;
        Assert.Equal(0, track.Gain);
    }

    [Theory]
    [InlineData("h264", 30, 90000000, true)]
    [InlineData("H264", 120, 40000000, true)]
    [InlineData("hevc", 30, 5000000, false)]
    [InlineData("h264", 400, 5000000, false)]
    [InlineData("h264", 30, 500000000, false)]
    public void CompatibilityPreviewOnlyStreamCopiesBoundedH264(string codec,
        double duration, long bytes, bool expected)
    {
        Assert.Equal(expected, PlaybackPreviewPolicy.CanStreamCopy(codec, duration, bytes));
    }

    [Fact]
    public void LongPreviewIsCappedAndNoPreviewIsValidForNaN()
    {
        Assert.Equal(180d, PlaybackPreviewPolicy.PreviewSeconds(900));
        Assert.Equal(29.5, PlaybackPreviewPolicy.PreviewSeconds(29.5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlaybackPreviewPolicy.PreviewSeconds(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlaybackPreviewPolicy.PreviewSeconds(double.PositiveInfinity));
    }

    [Fact]
    public void AudioMixIsNonDestructiveMetadataOnly()
    {
        var clip = new ClipMetadata { OriginalFilePath = @"C:\Videos\clip.mkv", DurationSeconds = 30,
            AudioTracks = new()
            {
                new() { TrackIndex = 0, Name = "System", Gain = 1 },
                new() { TrackIndex = 1, Name = "Microphone", Gain = 1 }
            }
        };
        var path = clip.OriginalFilePath;
        clip.TrimStartSeconds = 3;
        clip.TrimEndSeconds = 25;
        clip.AudioTracks[1].IsMuted = true;
        Assert.Equal(path, clip.OriginalFilePath);
        Assert.Equal(22, clip.SelectedDurationSeconds);
        Assert.True(clip.AudioTracks[1].IsMuted);
    }
}
