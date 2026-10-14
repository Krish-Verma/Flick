using System;

namespace LocalClip.Core.Rules;

public class BitrateBudget
{
    public long TargetBytes { get; set; }
    public long MarginBytes { get; set; }
    public long AvailableBytes { get; set; }
    public int AudioBitrateBps { get; set; }
    public int VideoBitrateBps { get; set; }
    public int RecommendedWidth { get; set; }
    public int RecommendedHeight { get; set; }
    public int RetryCount { get; set; }
}

public static class BitrateBudgetCalculator
{
    public const long BYTES_PER_MB = 1_000_000L;
    public const long BYTES_PER_MIB = 1_048_576L;

    public const int VIDEO_RATE_FLOOR_BPS = 500_000;    // 500 kbps floor
    public const int AUDIO_RATE_FLOOR_BPS = 64_000;     // 64 kbps floor
    public const int AUDIO_RATE_DEFAULT_BPS = 128_000;  // 128 kbps standard

    public static long TargetMbToBytes(int targetMb)
    {
        return targetMb * BYTES_PER_MB;
    }

    public static long ComputeMarginBytes(long targetBytes)
    {
        // Margin: at least the larger of 5% of the cap or 256 KiB
        long fivePercent = (long)(targetBytes * 0.05);
        long min256KiB = 256L * 1024L;
        return Math.Max(fivePercent, min256KiB);
    }

    public static BitrateBudget Calculate(
        int targetMb,
        double durationSeconds,
        int sourceWidth = 1920,
        int sourceHeight = 1080,
        int retryAttempt = 0,
        long previousOvershootBytes = 0)
    {
        if (durationSeconds <= 0.01) durationSeconds = 0.01;

        long targetBytes = TargetMbToBytes(targetMb);
        long baseMargin = ComputeMarginBytes(targetBytes);

        // If retrying due to overshoot, subtract additional penalty based on overshoot + 5%
        long totalMargin = baseMargin;
        if (retryAttempt > 0 && previousOvershootBytes > 0)
        {
            totalMargin += (long)(previousOvershootBytes * 1.15) + (retryAttempt * 256L * 1024L);
        }

        long availableBytes = Math.Max(targetBytes - totalMargin, targetBytes / 2);

        // Compute total bits available
        double totalBits = availableBytes * 8.0;
        double totalBps = totalBits / durationSeconds;

        int audioBps = AUDIO_RATE_DEFAULT_BPS;
        if (totalBps < 1_200_000)
        {
            // Lower audio bitrate for very constrained budgets
            audioBps = 96_000;
        }
        if (totalBps < 800_000)
        {
            audioBps = AUDIO_RATE_FLOOR_BPS;
        }

        int videoBps = (int)(totalBps - audioBps);
        if (videoBps < VIDEO_RATE_FLOOR_BPS)
        {
            videoBps = VIDEO_RATE_FLOOR_BPS;
        }

        // Recommend resolution downscaling if bitrate is too low for 1080p
        int recWidth = sourceWidth;
        int recHeight = sourceHeight;

        if (videoBps < 1_800_000 && sourceHeight >= 1080)
        {
            recWidth = 1280;
            recHeight = 720;
        }
        if (videoBps < 800_000 && sourceHeight >= 720)
        {
            recWidth = 854;
            recHeight = 480;
        }

        return new BitrateBudget
        {
            TargetBytes = targetBytes,
            MarginBytes = totalMargin,
            AvailableBytes = availableBytes,
            AudioBitrateBps = audioBps,
            VideoBitrateBps = videoBps,
            RecommendedWidth = recWidth,
            RecommendedHeight = recHeight,
            RetryCount = retryAttempt
        };
    }
}
