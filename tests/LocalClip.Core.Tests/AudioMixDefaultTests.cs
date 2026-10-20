using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class AudioMixDefaultTests
{
    [Fact]
    public void ImportedMultiAudioDefaultsToOnlyFirstTrack()
    {
        Assert.False(AudioMixDefaults.ShouldMute(true, 3, 0));
        Assert.True(AudioMixDefaults.ShouldMute(true, 3, 1));
        Assert.True(AudioMixDefaults.ShouldMute(true, 3, 2));
    }

    [Fact]
    public void NativeCombinedPreviewIsExcludedWhenDedicatedTracksExist()
    {
        Assert.True(AudioMixDefaults.ShouldMute(false, 3, 0));
        Assert.False(AudioMixDefaults.ShouldMute(false, 3, 1));
        Assert.False(AudioMixDefaults.ShouldMute(false, 3, 2));
        Assert.False(AudioMixDefaults.ShouldMute(false, 1, 0));
    }
}
