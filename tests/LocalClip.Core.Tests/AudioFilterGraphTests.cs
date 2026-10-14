using System;
using System.Collections.Generic;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.Core.Tests;

public class AudioFilterGraphTests
{
    [Fact]
    public void TestNullOrEmptyAudioTracksProducesSilentStereo()
    {
        string filterNull = ClipExporter.BuildAudioMixFilter(null);
        Assert.Equal("anullsrc=r=48000:cl=stereo[outa]", filterNull);

        string filterEmpty = ClipExporter.BuildAudioMixFilter(new List<AudioTrackInfo>());
        Assert.Equal("anullsrc=r=48000:cl=stereo[outa]", filterEmpty);
    }

    [Fact]
    public void TestAllMutedOrZeroGainProducesSilentStereo()
    {
        var tracks = new List<AudioTrackInfo>
        {
            new AudioTrackInfo { TrackIndex = 0, IsMuted = true, Gain = 1.0 },
            new AudioTrackInfo { TrackIndex = 1, IsMuted = false, Gain = 0.0001 } // Below threshold
        };

        string filter = ClipExporter.BuildAudioMixFilter(tracks);
        Assert.Equal("anullsrc=r=48000:cl=stereo[outa]", filter);
    }

    [Fact]
    public void TestSingleActiveTrackAppliesVolumeAndLimiter()
    {
        var tracks = new List<AudioTrackInfo>
        {
            new AudioTrackInfo { TrackIndex = 0, IsMuted = false, Gain = 1.25 },
            new AudioTrackInfo { TrackIndex = 1, IsMuted = true, Gain = 1.0 }
        };

        string filter = ClipExporter.BuildAudioMixFilter(tracks);
        Assert.Equal("[0:a:0]volume=1.25,alimiter=limit=0.98[outa]", filter);
    }

    [Fact]
    public void TestMultipleActiveTracksBuildsProperAmixGraph()
    {
        var tracks = new List<AudioTrackInfo>
        {
            new AudioTrackInfo { TrackIndex = 0, IsMuted = false, Gain = 1.0 },
            new AudioTrackInfo { TrackIndex = 1, IsMuted = false, Gain = 0.80 },
            new AudioTrackInfo { TrackIndex = 2, IsMuted = false, Gain = 1.50 }
        };

        string filter = ClipExporter.BuildAudioMixFilter(tracks);
        
        Assert.Contains("[0:a:0]volume=1.00[a0];", filter);
        Assert.Contains("[0:a:1]volume=0.80[a1];", filter);
        Assert.Contains("[0:a:2]volume=1.50[a2];", filter);
        Assert.Contains("[a0][a1][a2]amix=inputs=3:normalize=0,alimiter=limit=0.98[outa]", filter);
    }
}
