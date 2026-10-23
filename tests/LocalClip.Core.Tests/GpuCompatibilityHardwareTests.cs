using System.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Process;
using Xunit.Abstractions;

namespace LocalClip.Core.Tests;

/// <summary>
/// Real desktop recording tests. A passing unit suite never qualifies a GPU.
/// Run only on the owner's Windows desktop through test_gpu_compatibility.ps1.
/// </summary>
public sealed class GpuCompatibilityHardwareTests(ITestOutputHelper output)
{
    [HardwareFact]
    public Task AutomaticEncoderRecordsDecodableReplayOnExpectedHardware() => RecordReplayAsync(false);

    [HardwareFact]
    public Task SoftwareCompatibilityRecordsBoundedDecodableReplay() => RecordReplayAsync(true);

    private async Task RecordReplayAsync(bool software)
    {
        string evidenceRoot = Environment.GetEnvironmentVariable("FLICK_TEST_OUTPUT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "Flick-GpuCompatibility");
        string directory = Path.Combine(evidenceRoot,
            (software ? "software-" : "automatic-") + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = AppSettings.CreateDefault();
        settings.OriginalsFolder = directory;
        settings.ExportsFolder = Path.Combine(directory, "Exports");
        settings.TempFolder = Path.Combine(directory, "Temp");
        settings.EnableMicrophone = false;
        settings.VideoEncoder = software ? "obs_x264" : "auto";
        settings.AllowSoftwareFallback = true;
        // Starting from Smooth proves the CPU path reports and records its
        // reduced profile rather than claiming to have captured 1080p60.
        RecordingProfiles.TryApply(software ? RecordingProfiles.Smooth : RecordingProfiles.Balanced, settings);
        settings.ReplayDurationSeconds = 15;
        settings.MonitorIndex = int.TryParse(Environment.GetEnvironmentVariable("FLICK_TEST_MONITOR"), out int index)
            ? index : 0;
        using var client = new CaptureHostClient();
        client.Start();
        try
        {
            Assert.True(await client.StartCaptureAsync(settings), client.LastStartError);
            var profile = Assert.IsType<CaptureEncodingProfile>(client.CurrentProfile);
            output.WriteLine($"Actual recording: {profile.EncoderId}, {profile.EncoderName}, " +
                $"{profile.Width}x{profile.Height}@{profile.Fps}, software={profile.IsSoftwareFallback}");
            if (software)
            {
                Assert.Equal("obs_x264", profile.EncoderId);
                Assert.True(profile.IsSoftwareFallback);
                Assert.InRange(profile.Width, 320, 1280);
                Assert.InRange(profile.Height, 240, 720);
                Assert.InRange(profile.Fps, 1, 30);
            }
            else
            {
                string expected = Environment.GetEnvironmentVariable("FLICK_EXPECT_ENCODER") ?? "";
                Assert.False(string.IsNullOrWhiteSpace(expected),
                    "Set FLICK_EXPECT_ENCODER to the exact hardware encoder ID being qualified (or obs_x264 for no hardware encoder). " +
                    "Otherwise a CPU fallback could falsely qualify a GPU.");
                Assert.Equal(expected, profile.EncoderId);
                Assert.Equal(expected == "obs_x264", profile.IsSoftwareFallback);
            }

            await Task.Delay(TimeSpan.FromSeconds(17));
            var status = await client.GetStatusAsync();
            Assert.Equal("Armed", status.State);
            Assert.True(status.TargetValid);
            Assert.Equal(profile, status.Profile);
            var saved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            string saveId = Guid.NewGuid().ToString("N");
            client.ReplaySaved += (observedId, path) =>
            {
                if (observedId == saveId) saved.TrySetResult(path);
            };
            Assert.True(await client.SaveReplayAsync(saveId, "Compatibility"));
            string file = await saved.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(await client.StopCaptureAsync());
            Assert.Equal("Off", (await client.GetStatusAsync()).State);

            var prober = new MediaProber(
                Environment.GetEnvironmentVariable("FLICK_TEST_FFPROBE"),
                Environment.GetEnvironmentVariable("FLICK_TEST_FFMPEG"));
            var probe = await prober.ProbeFileAsync(file);
            Assert.True(probe.IsValid, probe.ErrorMessage);
            Assert.Equal("h264", probe.VideoCodec);
            Assert.Equal(profile.Width, probe.Width);
            Assert.Equal(profile.Height, probe.Height);
            Assert.Equal(profile.Fps, probe.Fps);
            Assert.InRange(probe.DurationSeconds, 12, 18);
            Assert.NotEmpty(probe.AudioTracks);
            await DecodeReplayAsync(file);
            // Sharing must work on an AMD/Intel/CPU-only machine even with
            // the separate overshoot preference disabled (the default).
            settings.UseTwoPassSoftwareIfHardwareOvershoots = false;
            foreach (var track in probe.AudioTracks) track.IsMuted = track.TrackIndex != 0;
            var clip = new ClipMetadata {
                OriginalFilePath = file, DurationSeconds = probe.DurationSeconds,
                TrimStartSeconds = 0, TrimEndSeconds = probe.DurationSeconds,
                Width = probe.Width, Height = probe.Height, AudioTracks = probe.AudioTracks
            };
            var exporter = new ClipExporter(Environment.GetEnvironmentVariable("FLICK_TEST_FFMPEG"), prober);
            var export = await exporter.ExportForDiscordAsync(clip, settings);
            Assert.True(export.Success, export.ErrorMessage);
            Assert.InRange(export.FileSizeBytes, 1L, settings.DiscordTargetSizeMb * 1_000_000L);
            var exported = await prober.ProbeFileAsync(export.ExportFilePath);
            Assert.True(exported.IsValid, exported.ErrorMessage);
            Assert.Equal("h264", exported.VideoCodec);
            Assert.Equal("aac", Assert.Single(exported.AudioTracks).Codec);
            await DecodeReplayAsync(export.ExportFilePath);
            output.WriteLine("Saved replay retained for visual/audio review: " + file);
        }
        finally
        {
            await client.StopCaptureAsync();
            await client.ShutdownAsync();
        }
    }

    private static async Task DecodeReplayAsync(string file)
    {
        string executable = Environment.GetEnvironmentVariable("FLICK_TEST_FFMPEG") ?? "ffmpeg.exe";
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (string value in new[] { "-v", "error", "-xerror", "-i", file,
            "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-" })
            start.ArgumentList.Add(value);
        using var worker = new System.Diagnostics.Process { StartInfo = start };
        using var job = new JobObject();
        worker.Start();
        if (!job.AssignProcess(worker) && !worker.HasExited)
        {
            try { worker.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Could not confine compatibility decoder");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var stop = timeout.Token.Register(() =>
        {
            try { if (!worker.HasExited) worker.Kill(entireProcessTree: true); } catch { }
        });
        var errors = worker.StandardError.ReadToEndAsync(timeout.Token);
        var stdout = worker.StandardOutput.ReadToEndAsync(timeout.Token);
        await worker.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, worker.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(await errors), await errors);
        _ = await stdout;
    }
}
