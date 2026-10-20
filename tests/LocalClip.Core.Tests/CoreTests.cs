using System;
using System.Collections.Generic;
using System.IO;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Sharing;
using Xunit;

namespace LocalClip.Core.Tests;

public class CoreTests
{
    [Fact]
    public void TestMbVsMibDistinction()
    {
        Assert.Equal(1_000_000L, BitrateBudgetCalculator.BYTES_PER_MB);
        Assert.Equal(1_048_576L, BitrateBudgetCalculator.BYTES_PER_MIB);
        Assert.Equal(20_000_000L, BitrateBudgetCalculator.TargetMbToBytes(20));
        Assert.Equal(50_000_000L, BitrateBudgetCalculator.TargetMbToBytes(50));
    }

    [Fact]
    public void TestMarginCalculation()
    {
        // 20 MB = 20,000,000 bytes. 5% is 1,000,000 bytes. Larger than 256 KiB (262,144).
        long margin20Mb = BitrateBudgetCalculator.ComputeMarginBytes(20_000_000L);
        Assert.Equal(1_000_000L, margin20Mb);

        // Very small target, e.g. 1 MB (1,000,000 bytes). 5% is 50,000 bytes.
        // Minimum margin must be 256 KiB = 262,144 bytes.
        long marginSmall = BitrateBudgetCalculator.ComputeMarginBytes(1_000_000L);
        Assert.Equal(256L * 1024L, marginSmall);
    }

    [Theory]
    [InlineData(20, 30.0)]
    [InlineData(50, 60.0)]
    [InlineData(10, 15.0)]
    public void TestBitrateBudgetCalculation(int targetMb, double durationSec)
    {
        var budget = BitrateBudgetCalculator.Calculate(targetMb, durationSec, 1920, 1080);
        Assert.True(budget.AvailableBytes < budget.TargetBytes);
        Assert.True(budget.VideoBitrateBps >= BitrateBudgetCalculator.VIDEO_RATE_FLOOR_BPS);
        Assert.True(budget.AudioBitrateBps >= BitrateBudgetCalculator.AUDIO_RATE_FLOOR_BPS);

        // Total bits over duration must fit inside available bytes
        double totalBits = (budget.VideoBitrateBps + budget.AudioBitrateBps) * durationSec;
        long estimatedBytes = (long)(totalBits / 8.0);
        Assert.True(estimatedBytes <= budget.TargetBytes);
    }

    [Fact]
    public void TestBitrateBudgetDownscalingForLowBitrate()
    {
        // 5 MB over 120 seconds -> very low bitrate -> should recommend downscaling from 1080p to 720p or 480p
        var budget = BitrateBudgetCalculator.Calculate(5, 120.0, 1920, 1080);
        Assert.True(budget.RecommendedHeight < 1080);
    }

    [Fact]
    public void TestBitrateBudgetRetryPenalty()
    {
        var budget1 = BitrateBudgetCalculator.Calculate(20, 30.0, 1920, 1080, retryAttempt: 0);
        var budget2 = BitrateBudgetCalculator.Calculate(20, 30.0, 1920, 1080, retryAttempt: 1, previousOvershootBytes: 500_000);

        Assert.True(budget2.AvailableBytes < budget1.AvailableBytes);
        Assert.True(budget2.VideoBitrateBps < budget1.VideoBitrateBps);
    }

    [Fact]
    public void TestTrimValidatorValidCases()
    {
        var res1 = TrimValidator.Validate(0.0, 30.0, 30.0);
        Assert.True(res1.IsValid);
        Assert.Equal(0.0, res1.ValidatedStart);
        Assert.Equal(30.0, res1.ValidatedEnd);
        Assert.Equal(30.0, res1.ValidatedDuration);

        var res2 = TrimValidator.Validate(5.0, 25.0, 30.0);
        Assert.True(res2.IsValid);
        Assert.Equal(5.0, res2.ValidatedStart);
        Assert.Equal(25.0, res2.ValidatedEnd);
        Assert.Equal(20.0, res2.ValidatedDuration);
    }

    [Fact]
    public void TestTrimValidatorInvalidCases()
    {
        // Negative start
        var res1 = TrimValidator.Validate(-2.0, 10.0, 30.0);
        Assert.False(res1.IsValid);

        // Start >= End
        var res2 = TrimValidator.Validate(15.0, 10.0, 30.0);
        Assert.False(res2.IsValid);

        // End > OriginalDuration
        var res3 = TrimValidator.Validate(0.0, 35.0, 30.0);
        Assert.False(res3.IsValid);

        // Duration too short (< 0.25s)
        var res4 = TrimValidator.Validate(5.0, 5.1, 30.0);
        Assert.False(res4.IsValid);
    }

    [Fact]
    public void TestExportFingerprintSensitivity()
    {
        var tracks1 = new List<AudioTrackInfo>
        {
            new AudioTrackInfo { StreamIndex = 1, TrackIndex = 0, Name = "Combined", IsMuted = false, Gain = 1.0 },
            new AudioTrackInfo { StreamIndex = 2, TrackIndex = 1, Name = "Mic", IsMuted = false, Gain = 1.0 }
        };

        string fpBase = ExportFingerprint.Compute("test.mkv", 0.0, 30.0, tracks1, 20);

        // Change start time -> must change
        string fpStart = ExportFingerprint.Compute("test.mkv", 2.0, 30.0, tracks1, 20);
        Assert.NotEqual(fpBase, fpStart);

        // Change end time -> must change
        string fpEnd = ExportFingerprint.Compute("test.mkv", 0.0, 25.0, tracks1, 20);
        Assert.NotEqual(fpBase, fpEnd);

        // Change target size -> must change
        string fpSize = ExportFingerprint.Compute("test.mkv", 0.0, 30.0, tracks1, 50);
        Assert.NotEqual(fpBase, fpSize);

        // Mute mic -> must change
        var tracksMuted = new List<AudioTrackInfo>
        {
            new AudioTrackInfo { StreamIndex = 1, TrackIndex = 0, Name = "Combined", IsMuted = false, Gain = 1.0 },
            new AudioTrackInfo { StreamIndex = 2, TrackIndex = 1, Name = "Mic", IsMuted = true, Gain = 1.0 }
        };
        string fpMuted = ExportFingerprint.Compute("test.mkv", 0.0, 30.0, tracksMuted, 20);
        Assert.NotEqual(fpBase, fpMuted);

        // Gain change -> must change
        var tracksGain = new List<AudioTrackInfo>
        {
            new AudioTrackInfo { StreamIndex = 1, TrackIndex = 0, Name = "Combined", IsMuted = false, Gain = 1.0 },
            new AudioTrackInfo { StreamIndex = 2, TrackIndex = 1, Name = "Mic", IsMuted = false, Gain = 1.5 }
        };
        string fpGain = ExportFingerprint.Compute("test.mkv", 0.0, 30.0, tracksGain, 20);
        Assert.NotEqual(fpBase, fpGain);
    }

    [Fact]
    public void TestDpapiSecretProtection()
    {
        string secret = "https://discord.com/api/webhooks/123456789/abcdefABCDEF-secret";
        string encrypted = DiscordWebhookClient.ProtectSecret(secret);
        Assert.False(string.IsNullOrEmpty(encrypted));
        Assert.NotEqual(secret, encrypted);

        string decrypted = DiscordWebhookClient.UnprotectSecret(encrypted);
        Assert.Equal(secret, decrypted);
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/1234567890/abc123XYZ_-", true)]
    [InlineData("https://canary.discord.com/api/webhooks/987654321/secret_token", true)]
    [InlineData("https://ptb.discordapp.com/api/v10/webhooks/11223344/token", true)]
    [InlineData("http://discord.com/api/webhooks/123456/token", false)] // No HTTP
    [InlineData("https://malicious.com/api/webhooks/123456/token", false)] // No non-discord
    [InlineData("https://discord.com/api/users/@me", false)] // No user tokens
    [InlineData("", false)]
    public void TestDiscordWebhookUrlValidation(string url, bool expectedValid)
    {
        bool isValid = DiscordWebhookClient.ValidateWebhookUrl(url, out string err);
        Assert.Equal(expectedValid, isValid);
    }
    [Fact]
    public void TestShortClipHighCustomCapCannotOverflowBitrate()
    {
        var budget = LocalClip.Core.Rules.BitrateBudgetCalculator.Calculate(250, 0.25);
        Assert.InRange(budget.VideoBitrateBps,
            LocalClip.Core.Rules.BitrateBudgetCalculator.VIDEO_RATE_FLOOR_BPS,
            LocalClip.Core.Rules.BitrateBudgetCalculator.VIDEO_RATE_CEILING_BPS);
        Assert.True(budget.VideoBitrateBps > 0);
        Assert.Equal(250_000_000L, budget.TargetBytes);
    }

}
