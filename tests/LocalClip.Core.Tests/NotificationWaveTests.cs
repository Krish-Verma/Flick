using System;
using System.IO;
using System.Text;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using System.Text.Json;
using Xunit;

namespace LocalClip.Core.Tests;

public class NotificationWaveTests
{
    private static byte[] Wave(int samples = 8000)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        writer.Write(new byte[samples * 2]);
        return stream.ToArray();
    }

    [Fact]
    public void AcceptsPcmUpToThreeSecondsAndReportsActualDuration()
    {
        Assert.True(NotificationWaveRules.TryGetDuration(Wave(24000), out var duration));
        Assert.Equal(3, duration);
        Assert.False(NotificationWaveRules.IsValid(Wave(24001)));
    }

    [Fact]
    public void RejectsTruncatedOverflowAndDisguisedNonPcmPayloads()
    {
        var wave = Wave();
        Assert.False(NotificationWaveRules.IsValid(wave.AsSpan(0, wave.Length - 1)));
        wave[20] = 3; // float WAV is deliberately not accepted by this PCM-only path
        Assert.False(NotificationWaveRules.IsValid(wave));
        wave = Wave(); Array.Fill<byte>(wave, 255, 40, 4);
        Assert.False(NotificationWaveRules.IsValid(wave));
        Assert.False(NotificationWaveRules.IsValid(new byte[NotificationWaveRules.MaximumBytes + 1]));
    }

    [Fact]
    public void RejectsInconsistentByteRateAndEmptyData()
    {
        var wave = Wave(); wave[28] = 0;
        Assert.False(NotificationWaveRules.IsValid(wave));
        Assert.False(NotificationWaveRules.IsValid(Wave(0)));
    }

    [Theory]
    [InlineData(29.6, "30 sec")]
    [InlineData(15.0, "15 sec")]
    [InlineData(0.5, "1 sec")]
    [InlineData(-1, "0 sec")]
    [InlineData(double.NaN, "0 sec")]
    public void BannerDetailContainsOnlyRoundedActualDuration(double seconds, string expected) =>
        Assert.Equal(expected, NotificationWaveRules.DurationText(seconds));

    [Fact]
    public void SoundPreferenceRoundTripIsIndependentOfBannerAndRecording()
    {
        var settings = new AppSettings { ClipSavedSoundTone = "Custom", ClipSavedSoundPath = @"C:\Sounds\quiet.wav", PlayClipSavedSound = false, ShowClipSavedOverlay = true };
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("Custom", copy.ClipSavedSoundTone);
        Assert.Equal(settings.ClipSavedSoundPath, copy.ClipSavedSoundPath);
        Assert.False(copy.PlayClipSavedSound);
        Assert.True(copy.ShowClipSavedOverlay);
        Assert.False(copy.StartArmed);
    }
}
