using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Process;
using LocalClip.Core.Rules;
using Xunit.Abstractions;

namespace LocalClip.Core.Tests;

/// <summary>
/// Exercises production probing, FFmpeg arguments, encoding, validation and
/// atomic promotion. Inputs are generated patterns/tones, never capture devices.
/// Windows CI deliberately has no NVENC device, exercising the real CPU retry.
/// </summary>
public sealed class MediaWorkflowTests(ITestOutputHelper output)
{
    [MediaFact]
    public async Task ProbeAndTrimSelectedAudioThroughRealCpuFallback()
    {
        await using var fixture = await GeneratedMedia.CreateAsync(output);
        var media = await fixture.Prober.ProbeFileAsync(fixture.Source);
        Assert.True(media.IsValid, media.ErrorMessage);
        Assert.Equal((1280, 720, 30), (media.Width, media.Height, media.Fps));
        Assert.InRange(media.DurationSeconds, 7.95, 8.10);
        Assert.Equal(2, media.AudioTracks.Count);
        Assert.Equal("Generated 440 Hz", media.AudioTracks[0].Name);
        Assert.Equal("Generated 880 Hz", media.AudioTracks[1].Name);
        Assert.True(media.AudioTracks[0].IsMuted);
        Assert.False(media.AudioTracks[1].IsMuted);

        var clip = fixture.Clip(media, 4.25, 6.75);
        clip.AudioTracks[1].Gain = 0.35;
        var settings = fixture.Settings(1);
        Assert.False(settings.UseTwoPassSoftwareIfHardwareOvershoots);
        var exporter = new ObservedExporter(fixture.Ffmpeg, fixture.Prober);
        var result = await exporter.ExportForDiscordAsync(clip, settings);
        var exported = await fixture.AssertShareAsync(result, 2.5, 1);

        Assert.Contains(exporter.Encodes, attempt => !attempt.Software && !attempt.Success);
        Assert.Contains(exporter.Encodes, attempt => attempt.Software && attempt.Success);
        Assert.InRange(result.AttemptsUsed, 2, 3);
        Assert.Equal("aac", Assert.Single(exported.AudioTracks).Codec);
        // The corner changes from red to blue at t=4 in the source. Duration
        // alone cannot catch an export that ignored the nonzero trim start.
        await fixture.AssertCornerAsync(result.ExportFilePath, 0.25, blue: true);
        float[] samples = await fixture.ReadAudioAsync(result.ExportFilePath);
        double selected = ToneAmplitude(samples, 880), rejected = ToneAmplitude(samples, 440);
        output.WriteLine($"Selected 880 Hz amplitude={selected:F6}; rejected 440 Hz={rejected:F6}");
        Assert.InRange(selected, 0.015, 0.08);
        Assert.True(rejected < selected * 0.05, "The muted 440 Hz track leaked into the selected audio.");
        await fixture.AssertOriginalUnchangedAsync();
        fixture.AssertNoTemporaryExports();
    }

    [MediaFact]
    public async Task MutedAudioProducesSilentAacAndOriginalsStayImmutable()
    {
        await using var fixture = await GeneratedMedia.CreateAsync(output);
        var media = await fixture.Prober.ProbeFileAsync(fixture.Source);
        var clip = fixture.Clip(media, 0, 8);
        foreach (var track in clip.AudioTracks) track.IsMuted = true;
        var result = await new ClipExporter(fixture.Ffmpeg, fixture.Prober)
            .ExportForDiscordAsync(clip, fixture.Settings(1));
        var exported = await fixture.AssertShareAsync(result, 8, 1);
        Assert.Equal((854, 480), (exported.Width, exported.Height));
        float[] samples = await fixture.ReadAudioAsync(result.ExportFilePath);
        double rms = Math.Sqrt(samples.Average(sample => (double)sample * sample));
        output.WriteLine($"All-muted decoded AAC RMS={rms:E4}");
        Assert.True(rms < 0.00001, "All-muted export contains audible generated audio.");
        await fixture.AssertOriginalUnchangedAsync();
        fixture.AssertNoTemporaryExports();
    }

    [MediaFact]
    public async Task CachedExportIsRevalidatedAndTrimChangesProduceNewMedia()
    {
        await using var fixture = await GeneratedMedia.CreateAsync(output);
        var clip = fixture.Clip(await fixture.Prober.ProbeFileAsync(fixture.Source), 1, 3);
        var settings = fixture.Settings(2);
        var exporter = new ObservedExporter(fixture.Ffmpeg, fixture.Prober);
        var first = await exporter.ExportForDiscordAsync(clip, settings);
        await fixture.AssertShareAsync(first, 2, 2);
        await fixture.AssertCornerAsync(first.ExportFilePath, 0.25, blue: false);
        byte[] firstHash = await HashAsync(first.ExportFilePath);
        clip.ExportedFilePath = first.ExportFilePath;
        clip.LastExportFingerprint = first.Fingerprint;
        clip.IsExportReady = true;

        int encodedBeforeReuse = exporter.Encodes.Count;
        var reused = await exporter.ExportForDiscordAsync(clip, settings);
        Assert.True(reused.Success, reused.ErrorMessage);
        Assert.Equal(0, reused.AttemptsUsed);
        Assert.Equal(encodedBeforeReuse, exporter.Encodes.Count);
        Assert.True(exporter.ProbeCalls >= 2, "Cached export must be probed again.");
        Assert.True(exporter.DecodeCalls >= 2, "Cached export must be decoded again.");
        Assert.Equal(firstHash, await HashAsync(reused.ExportFilePath));

        // Matching metadata cannot make a truncated cached file share-ready.
        await File.WriteAllBytesAsync(reused.ExportFilePath, [0, 1, 2, 3]);
        var repaired = await exporter.ExportForDiscordAsync(clip, settings);
        await fixture.AssertShareAsync(repaired, 2, 2);
        Assert.InRange(repaired.AttemptsUsed, 1, 3);

        clip.TrimStartSeconds = 5;
        clip.TrimEndSeconds = 7;
        var changed = await exporter.ExportForDiscordAsync(clip, settings);
        await fixture.AssertShareAsync(changed, 2, 2);
        Assert.InRange(changed.AttemptsUsed, 1, 3);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
        Assert.False(firstHash.SequenceEqual(await HashAsync(changed.ExportFilePath)));
        await fixture.AssertCornerAsync(changed.ExportFilePath, 0.25, blue: true);
        await fixture.AssertOriginalUnchangedAsync();
        fixture.AssertNoTemporaryExports();
    }

    [MediaFact]
    public async Task CancellationAfterRealEncodeCleansTemporaryOutputAndReleasesGate()
    {
        await using var fixture = await GeneratedMedia.CreateAsync(output);
        var clip = fixture.Clip(await fixture.Prober.ProbeFileAsync(fixture.Source), 0, 3);
        var settings = fixture.Settings(2);
        using var cancellation = new CancellationTokenSource();
        // Cancel at a deterministic boundary after a genuine FFmpeg CPU worker
        // wrote a nonempty MP4, before production validation/atomic promotion.
        // This tests output cleanup without machine-speed-dependent timers.
        var exporter = new ObservedExporter(fixture.Ffmpeg, fixture.Prober) {
            AfterSoftwareEncode = path => {
                Assert.True(new FileInfo(path).Length > 1024);
                cancellation.Cancel();
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            exporter.ExportForDiscordAsync(clip, settings, ct: cancellation.Token));
        Assert.Contains(exporter.Encodes, attempt => attempt.Software && attempt.Success);
        Assert.Empty(Directory.EnumerateFiles(settings.ExportsFolder));
        await fixture.AssertOriginalUnchangedAsync();

        // A leaked static export semaphore would hang the next real operation.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var retry = await new ClipExporter(fixture.Ffmpeg, fixture.Prober)
            .ExportForDiscordAsync(clip, settings, ct: deadline.Token);
        await fixture.AssertShareAsync(retry, 3, 2);
        fixture.AssertNoTemporaryExports();
    }

    [MediaFact]
    public async Task OddDimensionsAndRotationExportWithoutDistortion()
    {
        await using var fixture = await GeneratedMedia.CreateAsync(output, oddDimensions: true);
        var odd = await fixture.Prober.ProbeFileAsync(fixture.Source);
        Assert.True(odd.IsValid, odd.ErrorMessage);
        Assert.Equal((641, 359), (odd.Width, odd.Height));
        var clip = fixture.Clip(odd, 0, 3);
        var exportedOdd = await new ClipExporter(fixture.Ffmpeg, fixture.Prober)
            .ExportForDiscordAsync(clip, fixture.Settings(2));
        var even = await fixture.AssertShareAsync(exportedOdd, 3, 2);
        Assert.Equal(0, even.Width % 2);
        Assert.Equal(0, even.Height % 2);
        Assert.InRange((double)even.Width / even.Height, 641.0 / 359 - 0.02, 641.0 / 359 + 0.02);

        await using var landscape = await GeneratedMedia.CreateAsync(output);
        string rotatedPath = Path.Combine(landscape.Root, "generated portrait rotation.mp4");
        await landscape.RunFfmpegAsync("-v", "error", "-y", "-display_rotation:v:0", "90",
            "-i", landscape.Source, "-t", "3", "-map", "0:v:0", "-map", "0:a:0",
            "-c:v", "copy", "-c:a", "aac", rotatedPath);
        byte[] rotatedOriginalHash = await HashAsync(rotatedPath);
        var raw = await RunAsync(landscape.Ffprobe, "-v", "error", "-show_streams", "-of", "json", rotatedPath);
        using (var document = JsonDocument.Parse(raw.StandardOutput))
        {
            var video = document.RootElement.GetProperty("streams").EnumerateArray()
                .Single(stream => stream.GetProperty("codec_type").GetString() == "video");
            Assert.Contains(video.GetProperty("side_data_list").EnumerateArray(), item =>
                item.TryGetProperty("rotation", out var rotation) && Math.Abs(rotation.GetInt32()) == 90);
        }
        var portrait = await landscape.Prober.ProbeFileAsync(rotatedPath);
        Assert.True(portrait.IsValid, portrait.ErrorMessage);
        Assert.Equal((720, 1280), (portrait.Width, portrait.Height));
        var rotatedClip = landscape.Clip(portrait, 0, 2);
        rotatedClip.OriginalFilePath = rotatedPath;
        var exportedPortrait = await new ClipExporter(landscape.Ffmpeg, landscape.Prober)
            .ExportForDiscordAsync(rotatedClip, landscape.Settings(2));
        var rotated = await landscape.AssertShareAsync(exportedPortrait, 2, 2);
        Assert.InRange((double)rotated.Width / rotated.Height, 0.55, 0.58);
        Assert.Equal(rotatedOriginalHash, await HashAsync(rotatedPath));
        await fixture.AssertOriginalUnchangedAsync();
        await landscape.AssertOriginalUnchangedAsync();
        fixture.AssertNoTemporaryExports();
        landscape.AssertNoTemporaryExports();
    }

    private sealed record EncodeAttempt(bool Software, bool Success);

    // Every media operation delegates to the production implementation. Only
    // observations and the deterministic cancellation hook are added here.
    private sealed class ObservedExporter(string ffmpeg, MediaProber prober) : ClipExporter(ffmpeg, prober)
    {
        public List<EncodeAttempt> Encodes { get; } = [];
        public int ProbeCalls { get; private set; }
        public int DecodeCalls { get; private set; }
        public Action<string>? AfterSoftwareEncode { get; init; }
        protected override async Task<bool> RunFfmpegEncodeAsync(string inputPath, string outputPath,
            double startSec, double durationSec, BitrateBudget budget, string audioFilter,
            bool useSoftware, CancellationToken ct)
        {
            bool success = await base.RunFfmpegEncodeAsync(inputPath, outputPath, startSec,
                durationSec, budget, audioFilter, useSoftware, ct);
            Encodes.Add(new(useSoftware, success));
            if (useSoftware && success) AfterSoftwareEncode?.Invoke(outputPath);
            return success;
        }
        protected override Task<ProbeResult> ProbeExportAsync(string path, CancellationToken ct)
        { ProbeCalls++; return base.ProbeExportAsync(path, ct); }
        protected override Task<bool> VerifyFullDecodeAsync(string path, CancellationToken ct)
        { DecodeCalls++; return base.VerifyFullDecodeAsync(path, ct); }
    }

    private sealed class GeneratedMedia : IAsyncDisposable
    {
        public string Root { get; }
        public string Source { get; }
        public string Ffmpeg { get; }
        public string Ffprobe { get; }
        public MediaProber Prober { get; }
        private readonly ITestOutputHelper _output;
        private byte[] _originalHash = [];
        private readonly bool _retainEvidence;

        private GeneratedMedia(ITestOutputHelper output)
        {
            Assert.True(OperatingSystem.IsWindows(), "Enabled media gate requires Windows JobObject confinement.");
            Ffmpeg = Tool("FLICK_TEST_FFMPEG");
            Ffprobe = Tool("FLICK_TEST_FFPROBE");
            string? evidence = Environment.GetEnvironmentVariable("FLICK_MEDIA_OUTPUT_DIR");
            _retainEvidence = !string.IsNullOrWhiteSpace(evidence);
            Root = Path.Combine(evidence ?? Path.GetTempPath(), "Flick generated média " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Source = Path.Combine(Root, "generated original with spaces.mkv");
            Prober = new MediaProber(Ffprobe, Ffmpeg);
            _output = output;
        }

        public static async Task<GeneratedMedia> CreateAsync(ITestOutputHelper output, bool oddDimensions = false)
        {
            var fixture = new GeneratedMedia(output);
            try
            {
                foreach (string tool in new[] { fixture.Ffmpeg, fixture.Ffprobe })
                {
                    string expectedVersion = Environment.GetEnvironmentVariable("FLICK_TEST_FFMPEG_VERSION") ?? "";
                    Assert.Matches(@"^\d+\.\d+\.\d+$", expectedVersion);
                    var version = await RunAsync(tool, "-version");
                    string banner = System.Text.Encoding.UTF8.GetString(version.StandardOutput).Split('\n')[0];
                    Assert.Contains("version " + expectedVersion, banner);
                    output.WriteLine(banner.Trim());
                }
                string pattern = oddDimensions ? "testsrc=size=641x359:rate=30:duration=8" : "testsrc2=size=1280x720:rate=30:duration=8";
                await fixture.RunFfmpegAsync("-v", "error", "-y", "-f", "lavfi", "-i", pattern,
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=8",
                    "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=8",
                    "-map", "0:v:0", "-map", "1:a:0", "-map", "2:a:0",
                    "-vf", "drawbox=x=0:y=0:w=96:h=96:color=red:t=fill:enable='lt(t,4)',drawbox=x=0:y=0:w=96:h=96:color=blue:t=fill:enable='gte(t,4)'",
                    "-c:v", "libx264", "-preset", "ultrafast", "-threads", "2", "-crf", "28",
                    "-pix_fmt", oddDimensions ? "yuv444p" : "yuv420p", "-c:a", "pcm_s16le",
                    "-metadata:s:a:0", "title=Generated 440 Hz", "-metadata:s:a:1", "title=Generated 880 Hz",
                    "-t", "8", fixture.Source);
                fixture._originalHash = await HashAsync(fixture.Source);
                output.WriteLine("Generated-only evidence directory: " + fixture.Root);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public AppSettings Settings(int capMb)
        {
            var settings = AppSettings.CreateDefault();
            settings.ExportsFolder = Path.Combine(Root, "Exports");
            settings.DiscordTargetSizeMb = capMb;
            settings.UseTwoPassSoftwareIfHardwareOvershoots = false;
            return settings;
        }

        public ClipMetadata Clip(ProbeResult source, double start, double end) => new() {
            OriginalFilePath = Source, DurationSeconds = source.DurationSeconds,
            FileSizeBytes = source.FileSizeBytes, Width = source.Width, Height = source.Height,
            Fps = source.Fps, TrimStartSeconds = start, TrimEndSeconds = end, AudioTracks = source.AudioTracks
        };

        public async Task<ProbeResult> AssertShareAsync(ExportResult result, double duration, int capMb)
        {
            Assert.True(result.Success, result.ErrorMessage);
            Assert.InRange(result.FileSizeBytes, 1024, capMb * 1_000_000L);
            Assert.Equal(result.FileSizeBytes, new FileInfo(result.ExportFilePath).Length);
            var probe = await Prober.ProbeFileAsync(result.ExportFilePath);
            Assert.True(probe.IsValid, probe.ErrorMessage);
            Assert.Equal("h264", probe.VideoCodec);
            Assert.Equal("aac", Assert.Single(probe.AudioTracks).Codec);
            Assert.InRange(probe.DurationSeconds, duration - 0.12, duration + 0.15);
            await RunFfmpegAsync("-v", "error", "-xerror", "-i", result.ExportFilePath,
                "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-");
            _output.WriteLine($"Decoded export: {probe.Width}x{probe.Height}, {probe.DurationSeconds:F3}s, " +
                $"{result.FileSizeBytes}/{capMb * 1_000_000L} bytes, attempts={result.AttemptsUsed}");
            return probe;
        }

        public async Task AssertCornerAsync(string path, double seconds, bool blue)
        {
            var frame = await RunAsync(Ffmpeg, "-v", "error", "-ss", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-i", path, "-vf", "crop=2:2:32:32,scale=1:1", "-frames:v", "1",
                "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1");
            Assert.Equal(3, frame.StandardOutput.Length);
            int red = frame.StandardOutput[0], green = frame.StandardOutput[1], b = frame.StandardOutput[2];
            Assert.True(blue ? b > red + 100 && b > green + 100 : red > b + 100 && red > green + 100,
                $"Trimmed corner should be {(blue ? "blue" : "red")}; got RGB {red},{green},{b}.");
        }

        public async Task<float[]> ReadAudioAsync(string path)
        {
            var decoded = await RunAsync(Ffmpeg, "-v", "error", "-ss", "0.25", "-i", path,
                "-map", "0:a:0", "-t", "1", "-ac", "1", "-ar", "8000", "-f", "f32le", "pipe:1");
            Assert.Equal(0, decoded.StandardOutput.Length % sizeof(float));
            var samples = new float[decoded.StandardOutput.Length / sizeof(float)];
            Buffer.BlockCopy(decoded.StandardOutput, 0, samples, 0, decoded.StandardOutput.Length);
            Assert.InRange(samples.Length, 7900, 8100);
            return samples;
        }

        public async Task AssertOriginalUnchangedAsync() => Assert.Equal(_originalHash, await HashAsync(Source));
        public void AssertNoTemporaryExports()
        {
            string exports = Path.Combine(Root, "Exports");
            if (Directory.Exists(exports)) Assert.Empty(Directory.EnumerateFiles(exports, ".flick-export-*"));
        }
        public async Task RunFfmpegAsync(params string[] arguments) => _ = await RunAsync(Ffmpeg, arguments);
        public ValueTask DisposeAsync()
        {
            if (!_retainEvidence && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }
        private static string Tool(string variable)
        {
            string path = Environment.GetEnvironmentVariable(variable) ?? "";
            Assert.True(Path.IsPathFullyQualified(path) && File.Exists(path),
                $"Set {variable} to the existing pinned executable's absolute path; enabled media tests never skip missing tools.");
            return path;
        }
    }

    private static double ToneAmplitude(float[] samples, double frequency)
    {
        double real = 0, imaginary = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double angle = 2 * Math.PI * frequency * i / 8000;
            real += samples[i] * Math.Cos(angle);
            imaginary -= samples[i] * Math.Sin(angle);
        }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / samples.Length;
    }

    private sealed record ToolResult(byte[] StandardOutput, string StandardError);
    private static async Task<ToolResult> RunAsync(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new System.Diagnostics.Process { StartInfo = start };
        using var job = new JobObject();
        Assert.True(process.Start(), "Could not start pinned media tool.");
        if (!job.AssignProcess(process) && !process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Could not confine generated-media worker.");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var stop = timeout.Token.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });
        using var bytes = new MemoryStream();
        Task stdout = process.StandardOutput.BaseStream.CopyToAsync(bytes, timeout.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        await stdout;
        string errors = await stderr;
        Assert.True(process.ExitCode == 0, $"{Path.GetFileName(executable)} exited {process.ExitCode}: {errors}");
        Assert.True(string.IsNullOrWhiteSpace(errors), errors);
        return new ToolResult(bytes.ToArray(), errors);
    }

    private static async Task<byte[]> HashAsync(string path) => SHA256.HashData(await File.ReadAllBytesAsync(path));
}
