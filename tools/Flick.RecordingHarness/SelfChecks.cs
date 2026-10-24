using System.Buffers.Binary;

namespace Flick.RecordingHarness;

internal sealed record SelfCheckResult(string Id, bool Passed, string? Error);
internal sealed record SelfCheckReceipt(int SchemaVersion, int Expected, int Executed, int Passed, int Failed,
    int Skipped, bool Accepted, IReadOnlyList<SelfCheckResult> Cases);

internal static class SelfChecks
{
    public const int ExpectedCount = 20;
    public static SelfCheckReceipt Run()
    {
        string root = Path.GetPathRoot(Environment.CurrentDirectory)!;
        string output = Path.Combine(root, HarnessOptions.OutputPrefix + Guid.NewGuid().ToString("N"));
        string[] valid = ["--host", Path.Combine(root, "host.exe"), "--ffmpeg", Path.Combine(root, "ffmpeg.exe"),
            "--ffprobe", Path.Combine(root, "ffprobe.exe"), "--output", output, "--encoder", "obs_nvenc_h264_tex", "--run-capture"];
        var results = new List<SelfCheckResult>();
        void Check(string id, Action action)
        {
            try { action(); results.Add(new(id, true, null)); }
            catch (Exception error) { results.Add(new(id, false, error.Message)); }
        }
        void Reject(Action action)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Unsafe/invalid input was accepted.");
        }
        string[] Replace(string key, string value)
        {
            var copy = valid.ToArray(); copy[Array.IndexOf(copy, key) + 1] = value; return copy;
        }
        Check("default-refuses", () => Reject(() => HarnessOptions.Parse([])));
        Check("confirmation-required", () => Reject(() => HarnessOptions.Parse(valid[..^1])));
        Check("required-paths", () => Reject(() => HarnessOptions.Parse(["--run-capture"])));
        Check("unknown-arguments", () => Reject(() => HarnessOptions.Parse([.. valid, "--desktop"])));
        Check("duplicate-path", () => Reject(() => HarnessOptions.Parse([.. valid, "--host", valid[1]])));
        Check("relative-path", () => Reject(() => HarnessOptions.Parse(Replace("--host", "host.exe"))));
        Check("auto-not-qualification", () => Reject(() => HarnessOptions.Parse(Replace("--encoder", "auto"))));
        Check("unknown-encoder", () => Reject(() => HarnessOptions.Parse(Replace("--encoder", "not-an-encoder"))));
        Check("owned-root-name", () => Reject(() => HarnessOptions.Parse(Replace("--output", Path.Combine(root, "ExistingLibrary")))));
        Check("valid-explicit-request", () =>
        {
            var parsed = HarnessOptions.Parse(valid);
            if (parsed.Encoder != "obs_nvenc_h264_tex" || parsed.Output != output) throw new InvalidDataException("Request changed.");
        });
        Check("hardware-fallback-disabled", () =>
        {
            var settings = QualificationSuite.CreateSettings(output, "obs_nvenc_h264_tex");
            if (settings.AllowSoftwareFallback || settings.EnableMicrophone || settings.EnableDiscordWebhook ||
                settings.TargetType != "Window" || !settings.TempFolder.StartsWith(output, StringComparison.Ordinal))
                throw new InvalidDataException("Unsafe fixture settings.");
        });
        Check("explicit-cpu-only", () =>
        {
            var settings = QualificationSuite.CreateSettings(output, "obs_x264");
            if (!settings.AllowSoftwareFallback || settings.VideoEncoder != "obs_x264") throw new InvalidDataException("CPU request changed.");
        });
        Check("silent-pcm", () => MediaVerification.ValidateSilence(new byte[8000 * 4]));
        Check("audible-pcm-refused", () =>
        {
            byte[] audio = new byte[8000 * 4];
            BinaryPrimitives.WriteInt32LittleEndian(audio, BitConverter.SingleToInt32Bits(0.1f));
            Reject(() => MediaVerification.ValidateSilence(audio));
        });
        Check("nonfinite-audio-refused", () =>
        {
            byte[] audio = new byte[8000 * 4];
            BinaryPrimitives.WriteInt32LittleEndian(audio, BitConverter.SingleToInt32Bits(float.NaN));
            Reject(() => MediaVerification.ValidateSilence(audio));
        });
        Check("incomplete-audio-refused", () => Reject(() => MediaVerification.ValidateSilence(new byte[32001])));
        Check("moving-owned-pattern", () => MediaVerification.ValidatePattern(PatternFrames(moving: true)));
        Check("frozen-pattern-refused", () => Reject(() => MediaVerification.ValidatePattern(PatternFrames(moving: false))));
        Check("black-frames-refused", () => Reject(() => MediaVerification.ValidatePattern(new byte[320 * 180 * 3 * 8])));
        Check("incorrect-colors-refused", () => Reject(() => MediaVerification.ValidatePattern(PatternFrames(moving: true, wrongColors: true))));
        bool accepted = results.Count == ExpectedCount && results.All(result => result.Passed);
        return new(1, ExpectedCount, results.Count, results.Count(r => r.Passed), results.Count(r => !r.Passed), 0, accepted, results);
    }

    private static byte[] PatternFrames(bool moving, bool wrongColors = false)
    {
        const int width = 320, height = 180, count = 8;
        byte[] frames = new byte[width * height * 3 * count];
        for (int frame = 0; frame < count; frame++)
        {
            void Pixel(int x, int y, byte r, byte g, byte b)
            {
                int offset = ((frame * height + y) * width + x) * 3;
                frames[offset] = r; frames[offset + 1] = g; frames[offset + 2] = b;
            }
            foreach (var sample in PatternWindow.Samples)
            {
                int cx = (int)(sample.XFraction * width), cy = (int)(sample.YFraction * height);
                for (int y = cy - 5; y <= cy + 5; y++)
                    for (int x = cx - 5; x <= cx + 5; x++)
                        Pixel(x, y, wrongColors ? sample.B : sample.R, sample.G, wrongColors ? sample.R : sample.B);
            }
            int markerX = 30 + (moving ? frame * 28 : 0);
            int markerY = (int)((PatternWindow.MotionBandTop + PatternWindow.MotionBandBottom) / 2 * height);
            for (int y = markerY - 5; y <= markerY + 5; y++)
                for (int x = markerX; x < markerX + 16; x++) Pixel(x, y, 248, 248, 248);
        }
        return frames;
    }
}
