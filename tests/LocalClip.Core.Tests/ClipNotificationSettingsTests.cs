using System.Text.Json;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.Core.Tests;

public class ClipNotificationSettingsTests
{
    [Fact]
    public void ClipFeedbackDefaultsEnabledButCaptureStartsDisarmed()
    {
        var settings = AppSettings.CreateDefault();
        Assert.True(settings.ShowClipSavedOverlay);
        Assert.True(settings.PlayClipSavedSound);
        Assert.False(settings.StartArmed);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void ClipFeedbackOptionsCanBeDisabledIndependentlyAndPersisted()
    {
        var settings = AppSettings.CreateDefault();
        settings.ShowClipSavedOverlay = false;
        settings.PlayClipSavedSound = true;
        var result = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));
        Assert.NotNull(result);
        Assert.False(result.ShowClipSavedOverlay);
        Assert.True(result.PlayClipSavedSound);
        result.PlayClipSavedSound = false;
        result.ShowClipSavedOverlay = true;
        var second = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(result));
        Assert.NotNull(second);
        Assert.True(second.ShowClipSavedOverlay);
        Assert.False(second.PlayClipSavedSound);
    }

    [Fact]
    public void OldSettingsWithoutNotificationKeysKeepPrivacySafeDefaults()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"SchemaVersion\":2,\"EnableMicrophone\":false}");
        Assert.NotNull(old);
        Assert.True(old.ShowClipSavedOverlay);
        Assert.True(old.PlayClipSavedSound);
        Assert.False(old.EnableMicrophone);
        Assert.False(old.StartArmed);
    }
}
