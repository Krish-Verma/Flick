using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.Core.Tests;

public class RecordingProfileTests
{
    [Theory]
    [InlineData("Light", 1280, 720, 30, 6000)]
    [InlineData("Balanced", 1920, 1080, 30, 15000)]
    [InlineData("Smooth", 1920, 1080, 60, 20000)]
    public void PresetsConfigureRealCaptureParameters(string preset, int width, int height, int fps, int kbps)
    {
        var settings = AppSettings.CreateDefault();
        Assert.True(RecordingProfiles.TryApply(preset, settings));
        Assert.Equal(preset, RecordingProfiles.Identify(settings));
        Assert.Equal(width, settings.VideoWidth);
        Assert.Equal(height, settings.VideoHeight);
        Assert.Equal(fps, settings.VideoFps);
        Assert.Equal(kbps, settings.VideoBitrateKbps);
        Assert.Equal("obs_nvenc_h264_tex", settings.VideoEncoder);
    }

    [Fact]
    public void InvalidPresetDoesNotChangeCaptureProfile()
    {
        var settings = AppSettings.CreateDefault();
        Assert.False(RecordingProfiles.TryApply("Ultra", settings));
        Assert.Equal(1920, settings.VideoWidth);
        Assert.Equal(15000, settings.VideoBitrateKbps);
    }

    [Fact]
    public void StartupAndMicrophoneRemainOptIn()
    {
        var settings = AppSettings.CreateDefault();
        Assert.False(settings.StartWithWindows);
        Assert.False(settings.StartArmed);
        Assert.False(settings.EnableMicrophone);
    }

    [Fact]
    public void LongHighQualityReplayProducesVisibleMemoryWarningEstimate()
    {
        var settings = AppSettings.CreateDefault();
        RecordingProfiles.TryApply(RecordingProfiles.Smooth, settings);
        Assert.True(RecordingProfiles.EstimatedReplayBytes(120, settings.VideoBitrateKbps, true)
            > settings.BufferMemoryLimitMb * 1_048_576L);
    }
}
