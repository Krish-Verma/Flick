using System.Text.Json;
using LocalClip.Core.Audio;
using LocalClip.Core.Ipc;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
namespace LocalClip.Core.Tests;

public sealed class AudioSeparationTests
{
    private static AudioProcessIdentity Source(uint pid = 42, ulong birth = 123) => new(pid, birth, @"C:\Apps\test.exe", "Synthetic app");
    [Fact] public void DiagnosticConsentFreezesAppRoutingWithoutWholeDeviceReplacement()
    {
        var settings = new AppSettings { AudioMode = "Separated", GameAudioSource = Source(), OriginalsFolder = Path.GetTempPath() };
        var target = new LocalClip.Core.Diagnostics.RecordingDiagnosticTarget("Window", "Synthetic window", WindowHwnd: 9, WindowPid: 42, WindowTitle: "Synthetic", WindowExecutable: "test.exe");
        var plan = LocalClip.Core.Diagnostics.RecordingDiagnosticPlan.Create(settings, target, enableSystemAudio: true);
        settings.AudioMode = "SystemFallback"; settings.GameAudioSource = null;
        var frozen = plan.CreateCaptureSettings(Path.Combine(Path.GetTempPath(), "SyntheticAudioNoIO"));
        Assert.Equal("Separated", frozen.AudioMode); Assert.Equal(Source(), frozen.GameAudioSource);
        Assert.Contains("selected app process trees", plan.AudioSummary);
        Assert.DoesNotContain("all audio", plan.AudioSummary); Assert.False(frozen.EnableMicrophone);
    }
    [Fact] public void LegacyPidsCannotAuthorizeSeparatedAudio()
    {
        var settings = new AppSettings { AudioMode = "Separated", GameProcessId = 42, DiscordProcessId = 43 };
        Assert.Throws<ArgumentException>(() => CaptureHostClient.CreateStartParameters(settings));
        Assert.False(settings.EnableMicrophone);
    }
    [Fact] public void ExactIdentitiesAreSentWithoutEnablingMicrophone()
    {
        var settings = new AppSettings { AudioMode = "Separated", GameAudioSource = Source(), VoiceAudioSource = Source(43, 124) };
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(CaptureHostClient.CreateStartParameters(settings)));
        Assert.Equal(42u, value.RootElement.GetProperty("game_audio").GetProperty("pid").GetUInt32());
        Assert.Equal(123ul, value.RootElement.GetProperty("game_audio").GetProperty("creation_time").GetUInt64());
        Assert.False(value.RootElement.GetProperty("enable_mic").GetBoolean());
    }
    [Fact] public void AudioOffDiagnosticDoesNotSendProcessTargets()
    {
        var settings = new AppSettings { AudioMode = "Separated", GameAudioSource = Source() };
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(CaptureHostClient.CreateStartParameters(settings, new(EnableSystemAudio: false))));
        Assert.False(value.RootElement.TryGetProperty("game_audio", out _));
        Assert.False(value.RootElement.GetProperty("enable_system_audio").GetBoolean());
    }
    [Fact] public void OverlappingIdenticalProcessesAreRejected()
    {
        var settings = new AppSettings { AudioMode = "Separated", GameAudioSource = Source(), VoiceAudioSource = Source() };
        Assert.Throws<ArgumentException>(() => AudioCapturePolicy.Validate(settings));
    }
    [Theory] [InlineData(0ul, "path")] [InlineData(123ul, "")]
    public void IncompleteProcessIdentityIsRejected(ulong birth, string path)
    {
        Assert.Throws<ArgumentException>(() => AudioCapturePolicy.Validate(new() { AudioMode = "Separated", GameAudioSource = new(42, birth, path, "test") }));
    }
    [Fact] public void AttachedDoesNotMeanSignalObserved()
    {
        using var json = JsonDocument.Parse("""{"audio_mode_actual":"Separated","audio_mode_requested":"separated","audio_sources":[{"kind":"Game","state":"Attached","process_id":42,"signal_observed":false}]}""");
        var status = CaptureAudioStatus.Parse(json.RootElement)!;
        Assert.Contains("no sound observed yet", status.Summary);
        Assert.False(status.Sources[0].SignalObserved);
    }
    [Fact] public void InvalidAudioStatusCannotClaimIsolation()
    {
        using var json = JsonDocument.Parse("""{"audio_mode_actual":"Separated","audio_sources":[{"kind":"Game","state":"Magic"}]}""");
        Assert.Throws<InvalidDataException>(() => CaptureAudioStatus.Parse(json.RootElement));
    }
    [Fact] public void CombinedAndDedicatedTracksAreNeverMixedTwice()
    {
        var tracks = new List<AudioTrackInfo> {
            new() { TrackIndex = 0, Role = AudioTrackRole.Combined }, new() { TrackIndex = 1, Role = AudioTrackRole.Game }, new() { TrackIndex = 2, Role = AudioTrackRole.Voice } };
        string filter = ClipExporter.BuildAudioMixFilter(tracks);
        Assert.Contains("[0:a:0]", filter); Assert.DoesNotContain("amix", filter);
        tracks[0].IsMuted = true;
        filter = ClipExporter.BuildAudioMixFilter(tracks);
        Assert.DoesNotContain("[0:a:0]", filter); Assert.Contains("amix=inputs=2", filter);
    }
    [Fact] public void ImportedTrackNamesDoNotProveRoles()
    {
        var clip = new ClipMetadata { SourceKind = "Imported", AudioTracks = [new() { TrackIndex = 0, Name = "Combined Audio" }, new() { TrackIndex = 1, Name = "Game Audio" }] };
        AudioTrackRoles.Reset(clip);
        Assert.False(clip.AudioTracks[0].IsMuted); Assert.True(clip.AudioTracks[1].IsMuted);
        Assert.All(clip.AudioTracks, track => Assert.Equal(AudioTrackRole.Unknown, track.Role));
    }
    [Fact] public void ResetDefaultsToRealStemsWithoutCombinedDuplication()
    {
        var clip = new ClipMetadata { AudioTracks = [new() { TrackIndex = 0, Role = AudioTrackRole.Combined }, new() { TrackIndex = 1, Role = AudioTrackRole.Game, Gain = .2, IsMuted = true }] };
        AudioTrackRoles.Reset(clip);
        Assert.True(clip.AudioTracks[0].IsMuted); Assert.False(clip.AudioTracks[1].IsMuted); Assert.Equal(1, clip.AudioTracks[1].Gain);
    }
    [Fact] public void FingerprintIncludesRoutingProvenance()
    {
        var tracks = new List<AudioTrackInfo> { new() { TrackIndex = 0 } };
        string unknown = ExportFingerprint.Compute("nonexistent-synthetic-input", 0, 1, tracks, 20);
        tracks[0].Role = AudioTrackRole.Combined;
        Assert.NotEqual(unknown, ExportFingerprint.Compute("nonexistent-synthetic-input", 0, 1, tracks, 20));
    }
}
