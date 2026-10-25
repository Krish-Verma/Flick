using LocalClip.Core.Audio;
using LocalClip.Core.Models;

namespace LocalClip.Core.Tests;

public sealed class WindowsAudioEndpointTests
{
    [Fact]
    public void UserSelectsActualPlaybackAndMicrophoneEndpointsInsteadOfChangingWindowsPlayback()
    {
        var outputs = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Output, "id-headset",
            new[] { new AudioEndpoint("id-speakers", "Speakers", true),
                    new AudioEndpoint("id-headset", "USB Headphones", false) });
        Assert.Equal("default", outputs[0].Id);
        Assert.Contains("Windows default", outputs[0].Label);
        Assert.Equal("id-headset", outputs.Single(x => x.Label.Contains("USB Headphones")).Id);
        Assert.True(outputs.Single(x => x.Id == "id-headset").IsAvailable);

        var microphones = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Input, "id-mic",
            new[] { new AudioEndpoint("id-mic", "USB Microphone", true) });
        Assert.Contains("communications microphone", microphones[0].Label);
        Assert.Equal("id-mic", microphones[1].Id);
        Assert.True(microphones[1].IsAvailable);
    }

    [Fact]
    public void MissingDeviceRemainsVisibleButUnavailableUntilUserExplicitlyChangesSelection()
    {
        var choices = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Output, "disconnected-headset",
            new[] { new AudioEndpoint("active-speakers", "Active Speakers", true) });
        var disconnected = Assert.Single(choices, x => x.Id == "disconnected-headset");
        Assert.False(disconnected.IsAvailable);
        Assert.Contains("unavailable", disconnected.Label, StringComparison.OrdinalIgnoreCase);
        Assert.True(choices[0].IsAvailable); // User can deliberately choose Windows default.
    }

    [Fact]
    public void DeviceChoicesStayBoundedAndDeDuplicateByEndpointId()
    {
        var duplicated = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Output, "default",
            new[] { new AudioEndpoint("a", "Z Speaker", false),
                    new AudioEndpoint("A", "A Speaker", true),
                    new AudioEndpoint("b", new string('x', 140), false),
                    new AudioEndpoint("default", "Duplicate default token", false) });
        Assert.Equal(3, duplicated.Count);
        Assert.Equal(2, duplicated.Where(x => x.Id != "default").Count());
        Assert.DoesNotContain(duplicated, x => x.Label.Length > 115);
    }

    [Fact]
    public void EndpointPreferencesAreSerializedAndMicrophoneRemainsOptIn()
    {
        var settings = AppSettings.CreateDefault();
        Assert.False(settings.EnableMicrophone);
        Assert.Equal("default", settings.SystemAudioDeviceId);
        Assert.Equal("default", settings.MicrophoneDeviceId);
        settings.SystemAudioDeviceId = "output-id";
        settings.MicrophoneDeviceId = "input-id";
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        var copy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal("output-id", copy.SystemAudioDeviceId);
        Assert.Equal("input-id", copy.MicrophoneDeviceId);
        Assert.False(copy.EnableMicrophone);
    }
}
