using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Process;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class AspectMediaWorkflowTests
{
    [MediaFact] public Task FitVerticalPreservesPictureAudioAndOriginal() => CheckAsync(ExportAspectPresets.Vertical9x16);
    [MediaFact] public Task FitSquarePreservesPictureAudioAndOriginal() => CheckAsync(ExportAspectPresets.Square1x1);

    private static async Task CheckAsync(string aspect)
    {
        string Tool(string name)
        {
            string path = Environment.GetEnvironmentVariable(name) ?? "";
            Assert.True(Path.IsPathFullyQualified(path) && File.Exists(path), "Enabled media gate requires pinned " + name);
            return path;
        }
        string ffmpeg = Tool("FLICK_TEST_FFMPEG"), ffprobe = Tool("FLICK_TEST_FFPROBE");
        string parent = Environment.GetEnvironmentVariable("FLICK_MEDIA_OUTPUT_DIR") ?? Path.GetTempPath();
        string root = Path.Combine(parent, "flick-fit-" + aspect + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string original = Path.Combine(root, "generated-source.mkv");
        await RunAsync(ffmpeg, "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "3", "-c:v", "libx264", "-threads", "2",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", original);
        byte[] hash = SHA256.HashData(await File.ReadAllBytesAsync(original));
        var prober = new MediaProber(ffprobe, ffmpeg);
        var source = await prober.ProbeFileAsync(original);
        Assert.True(source.IsValid, source.ErrorMessage);
        var clip = new ClipMetadata { OriginalFilePath = original, Width = source.Width, Height = source.Height,
            DurationSeconds = source.DurationSeconds, TrimEndSeconds = source.DurationSeconds,
            Fps = source.Fps, AudioTracks = source.AudioTracks, ExportAspectPreset = aspect };
        var settings = new AppSettings { ExportsFolder = Path.Combine(root, "Exports"), TempFolder = Path.Combine(root, "Temp"), DiscordTargetSizeMb = 1 };
        var exporter = new CpuExporter(ffmpeg, prober);
        var result = await exporter.ExportForDiscordAsync(clip, settings);
        Assert.True(result.Success, result.ErrorMessage); Assert.InRange(result.FileSizeBytes, 1, 1_000_000);
        var media = await prober.ProbeFileAsync(result.ExportFilePath);
        Assert.True(ExportAspectPresets.MatchesDimensions(aspect, media.Width, media.Height));
        Assert.Equal("aac", Assert.Single(media.AudioTracks).Codec);
        await RunAsync(ffmpeg, "-v", "error", "-xerror", "-nostdin", "-i", result.ExportFilePath, "-map", "0", "-f", "null", "-");
        byte[] frame = await RunAsync(ffmpeg, "-v", "error", "-nostdin", "-ss", "1", "-i", result.ExportFilePath,
            "-frames:v", "1", "-vf", "scale=32:32", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1");
        Assert.Equal(32 * 32 * 3, frame.Length);
        Assert.True(frame.Take(32 * 3 * 2).Average(value => (double)value) < 8, "Fit canvas must have black padding, not cropped/stretched source pixels.");
        Assert.True(frame.Skip(32 * 3 * 14).Take(32 * 3 * 4).Average(value => (double)value) > 20, "Source picture must remain visible in the fitted center.");
        byte[] json = await RunAsync(ffprobe, "-v", "error", "-show_streams", "-of", "json", result.ExportFilePath);
        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        double Number(JsonElement stream, string field) => stream.TryGetProperty(field, out var value) &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0;
        var video = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        var audio = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal("1:1", video.GetProperty("sample_aspect_ratio").GetString());
        Assert.InRange(Math.Abs(Number(video, "duration") - Number(audio, "duration")), 0, .15);
        Assert.InRange(Math.Abs(Number(video, "start_time") - Number(audio, "start_time")), 0, .05);
        byte[] after = SHA256.HashData(await File.ReadAllBytesAsync(original)); Assert.Equal(hash, after);
    }

    private sealed class CpuExporter(string ffmpeg, MediaProber prober) : ClipExporter(ffmpeg, prober)
    {
        protected override Task<bool> RunFfmpegEncodeAsync(string input, string output, double start, double duration,
            BitrateBudget budget, string audio, bool software, CancellationToken token) =>
            base.RunFfmpegEncodeAsync(input, output, start, duration, budget, audio, true, token);
    }
    private static async Task<byte[]> RunAsync(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new System.Diagnostics.Process { StartInfo = info };
        using var job = new JobObject();
        Assert.True(process.Start());
        if (!job.AssignProcess(process) && !process.HasExited) { process.Kill(true); throw new InvalidOperationException("Could not confine generated media worker."); }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var interrupt = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        using var output = new MemoryStream();
        Task stdout = process.StandardOutput.BaseStream.CopyToAsync(output, deadline.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
        Assert.True(process.ExitCode == 0, await stderr); return output.ToArray();
    }
}
