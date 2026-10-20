using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Ipc;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.Core.Tests;

public class IntegrationTests
{
    [HardwareFact]
    public async Task TestCaptureHostLifecycleAndIpc()
    {
        using var client = new CaptureHostClient();
        client.Start();

        // 1. Ping
        bool ping = await client.PingAsync();
        Assert.True(ping);

        // 2. Enum targets
        var targets = await client.GetTargetsAsync();
        Assert.NotNull(targets);
        Assert.NotEmpty(targets.Monitors);

        // 3. Status when idle
        var status = await client.GetStatusAsync();
        Assert.Equal("Off", status.State);
        Assert.False(status.IsArmed);

        // 4. Shutdown
        await client.ShutdownAsync();
        Assert.False(client.IsRunning);
    }

    [HardwareFact]
    public async Task TestRealCaptureAndExportPipeline()
    {
        string testOutputDir = Path.Combine(
            Environment.GetEnvironmentVariable("FLICK_TEST_OUTPUT_DIR")
                ?? Path.Combine(Path.GetTempPath(), "Flick-HardwareTests"),
            "Capture");
        Directory.CreateDirectory(testOutputDir);

        var settings = AppSettings.CreateDefault();
        settings.OriginalsFolder = testOutputDir;
        settings.ExportsFolder = Path.Combine(testOutputDir, "Exports");
        settings.TempFolder = Path.Combine(testOutputDir, "Temp");
        settings.DiscordTargetSizeMb = 20;
        settings.ReplayDurationSeconds = 30; // Validate full rolling replay duration
        settings.EnableMicrophone = true; // Explicit opt-in for this hardware-only test

        using var client = new CaptureHostClient();
        client.Start();

        string? savedClipPath = null;
        var saveTcs = new TaskCompletionSource<string>();

        client.ReplaySaved += (id, path) =>
        {
            savedClipPath = path;
            saveTcs.TrySetResult(path);
        };

        // 1. Start Capture
        bool started = await client.StartCaptureAsync(settings);
        Assert.True(started);

        // Allow the entire 30-second replay buffer to fill.
        await Task.Delay(TimeSpan.FromSeconds(31));

        // Query status while buffering
        var status = await client.GetStatusAsync();
        Assert.Contains(status.State, new[] { "Buffering", "Armed" });
        Assert.True(status.BufferedSec >= 29.0, $"Full 30s buffer not reached: {status.BufferedSec:F1}s");

        // 2. Trigger Save Replay
        string saveId = "test-clip-" + Guid.NewGuid().ToString("N");
        bool saveTriggered = await client.SaveReplayAsync(saveId, "IntTest");
        Assert.True(saveTriggered);

        // Wait for save callback
        var completedTask = await Task.WhenAny(saveTcs.Task, Task.Delay(15000));
        Assert.Equal(saveTcs.Task, completedTask);
        Assert.False(string.IsNullOrEmpty(savedClipPath));
        Assert.True(File.Exists(savedClipPath));

        // 3. Stop Capture
        await client.StopCaptureAsync();
        await client.ShutdownAsync();

        // 4. Probe original MKV
        var prober = new MediaProber();
        var origProbe = await prober.ProbeFileAsync(savedClipPath);
        Assert.True(origProbe.IsValid);
        Assert.InRange(origProbe.DurationSeconds, 28.0, 32.5);
        Assert.Equal(1920, origProbe.Width);
        Assert.Equal(1080, origProbe.Height);
        Assert.True(origProbe.AudioTracks.Count >= 3,
            $"Expected combined, system and mic tracks but received {origProbe.AudioTracks.Count}");

        // 5. Register with Storage
        var storage = new ClipStorage(settings, prober);
        var clip = await storage.RegisterNewOriginalAsync(savedClipPath, "Integration Test Monitor");
        Assert.NotNull(clip);
        Assert.True(File.Exists(clip.ThumbnailPath));

        // 6. Non-destructive Trim and Export for Discord (20 MB target)
        var exporter = new ClipExporter(prober: prober);
        clip.TrimStartSeconds = 0.0;
        clip.TrimEndSeconds = clip.DurationSeconds;

        var exportRes = await exporter.ExportForDiscordAsync(clip, settings);
        Assert.True(exportRes.Success, "Export failed: " + exportRes.ErrorMessage);
        Assert.True(File.Exists(exportRes.ExportFilePath));
        Assert.True(exportRes.FileSizeBytes <= 20_000_000L); // STRICT Discord 20 MB cap check

        // 7. Probe Exported MP4
        var exportProbe = await prober.ProbeFileAsync(exportRes.ExportFilePath);
        Assert.True(exportProbe.IsValid);
        Assert.Equal("h264", exportProbe.VideoCodec);
        Assert.Single(exportProbe.AudioTracks); // Exactly 1 mixed stereo track for Discord share
    }
}
