using System.Buffers.Binary;
using LocalClip.Core.Media;
using LocalClip.Core.Models;

namespace Flick.RecordingHarness;

internal sealed record PatternMetrics(int SampledFrames, int ValidColorFrames, int MovingMarkerFrames,
    double MarkerSpanPixels);
internal sealed record SilenceMetrics(int Samples, double Peak, double Rms);
internal sealed record MediaEvidence(string OriginalSha256, long Bytes, double DurationSeconds,
    int Width, int Height, int Fps, string AudioCodec, string ContainerAudioTrackName, string RegisteredAudioTrackName,
    PatternMetrics Pattern, SilenceMetrics Silence, bool FullDecodePassed);

internal static class MediaVerification
{
    private const int SampleWidth = 320, SampleHeight = 180;

    public static async Task<MediaEvidence> VerifyAsync(HarnessOptions options, ClipMetadata clip,
        CancellationToken cancellationToken)
    {
        string file = clip.OriginalFilePath;
        string before = await OwnedOutput.HashAsync(file, cancellationToken).ConfigureAwait(false);
        var prober = new MediaProber(options.Ffprobe, options.Ffmpeg);
        var probe = await prober.ProbeFileAsync(file, cancellationToken).ConfigureAwait(false);
        if (!probe.IsValid || !double.IsFinite(probe.DurationSeconds) || probe.DurationSeconds is < 2 or > 15 ||
            probe.Width != 640 || probe.Height != 360 || probe.Fps != 30 || probe.VideoCodec != "h264" ||
            probe.AudioTracks.Count != 1 || probe.AudioTracks[0].Codec != "aac" ||
            clip.AudioTracks.Count != 1 || clip.AudioTracks[0].Name != "Silent Diagnostic Audio")
            throw new InvalidDataException("Saved fixture does not match its bounded H.264 / silent AAC profile.");

        // Decode every video/audio packet, not merely a short thumbnail or probe.
        await ToolProcess.RunAsync(options.Ffmpeg,
            ["-v", "error", "-xerror", "-nostdin", "-i", file, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"],
            1024, cancellationToken).ConfigureAwait(false);

        var frames = await ToolProcess.RunAsync(options.Ffmpeg,
            ["-v", "error", "-xerror", "-nostdin", "-i", file, "-map", "0:v:0", "-vf", "fps=3,scale=320:180",
                "-frames:v", "45", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"],
            45 * SampleWidth * SampleHeight * 3, cancellationToken).ConfigureAwait(false);
        PatternMetrics pattern = ValidatePattern(frames.Output);
        var audio = await ToolProcess.RunAsync(options.Ffmpeg,
            ["-v", "error", "-xerror", "-nostdin", "-i", file, "-map", "0:a:0", "-ac", "1", "-ar", "8000",
                "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"],
            16 * 8000 * 4, cancellationToken).ConfigureAwait(false);
        SilenceMetrics silence = ValidateSilence(audio.Output);
        string after = await OwnedOutput.HashAsync(file, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before, after, StringComparison.Ordinal))
            throw new InvalidDataException("Verification changed the immutable original.");
        return new(before, new FileInfo(file).Length, probe.DurationSeconds, probe.Width, probe.Height,
            probe.Fps, probe.AudioTracks[0].Codec, probe.AudioTracks[0].Name, clip.AudioTracks[0].Name, pattern, silence, true);
    }

    internal static SilenceMetrics ValidateSilence(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length < 8000 * 4 || pcm.Length % 4 != 0)
            throw new InvalidDataException("Silent AAC did not decode to enough complete samples.");
        double peak = 0, squared = 0;
        for (int offset = 0; offset < pcm.Length; offset += 4)
        {
            float sample = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(pcm[offset..]));
            if (!float.IsFinite(sample)) throw new InvalidDataException("Audio decoder returned non-finite samples.");
            peak = Math.Max(peak, Math.Abs(sample));
            squared += (double)sample * sample;
        }
        int count = pcm.Length / 4;
        double rms = Math.Sqrt(squared / count);
        // -80 dBFS peak is well above AAC's decoded zero floor, but below audible captured content.
        if (peak > 0.0001 || rms > 0.00003)
            throw new InvalidDataException("No-audio fixture contains a non-silent decoded track.");
        return new(count, peak, rms);
    }

    internal static PatternMetrics ValidatePattern(ReadOnlySpan<byte> rgb)
    {
        int frameBytes = SampleWidth * SampleHeight * 3;
        if (rgb.Length % frameBytes != 0 || rgb.Length / frameBytes < 4)
            throw new InvalidDataException("Too few complete fixture frames were decoded.");
        int valid = 0, markerFrames = 0;
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        for (int frame = 0; frame < rgb.Length / frameBytes; frame++)
        {
            var pixels = rgb.Slice(frame * frameBytes, frameBytes);
            bool colorsValid = true;
            foreach (var sample in PatternWindow.Samples)
            {
                int x = Math.Clamp((int)(sample.XFraction * SampleWidth), 2, SampleWidth - 3);
                int y = Math.Clamp((int)(sample.YFraction * SampleHeight), 2, SampleHeight - 3);
                int red = 0, green = 0, blue = 0;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int index = ((y + dy) * SampleWidth + x + dx) * 3;
                        red += pixels[index]; green += pixels[index + 1]; blue += pixels[index + 2];
                    }
                if (Math.Abs(red / 25.0 - sample.R) > 55 || Math.Abs(green / 25.0 - sample.G) > 55 ||
                    Math.Abs(blue / 25.0 - sample.B) > 55) colorsValid = false;
            }
            if (colorsValid) valid++;
            long sumX = 0; int whiteCount = 0;
            int top = (int)(PatternWindow.MotionBandTop * SampleHeight);
            int bottom = (int)(PatternWindow.MotionBandBottom * SampleHeight);
            for (int y = top; y < bottom; y++)
                for (int x = 8; x < SampleWidth - 8; x++)
                {
                    int offset = (y * SampleWidth + x) * 3;
                    if (pixels[offset] > 220 && pixels[offset + 1] > 220 && pixels[offset + 2] > 220)
                    { sumX += x; whiteCount++; }
                }
            if (colorsValid && whiteCount >= 16)
            {
                double centroid = (double)sumX / whiteCount;
                minimum = Math.Min(minimum, centroid); maximum = Math.Max(maximum, centroid); markerFrames++;
            }
        }
        int count = rgb.Length / frameBytes;
        double span = markerFrames == 0 ? 0 : maximum - minimum;
        if (valid < 4 || valid < Math.Ceiling(count * 0.8) || markerFrames < 4 || span < 20)
            throw new InvalidDataException("Decoded frames did not prove the owned color pattern and moving marker.");
        return new(count, valid, markerFrames, span);
    }
}
