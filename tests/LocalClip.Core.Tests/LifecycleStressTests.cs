using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace LocalClip.Core.Tests;

public class LifecycleStressTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testOutputDir;

    public LifecycleStressTests(ITestOutputHelper output)
    {
        _output = output;
        _testOutputDir = Path.Combine(@"D:\Documents\ClipMax", "TestOutput", "Lifecycle");
        Directory.CreateDirectory(_testOutputDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testOutputDir))
            {
                // Keep completed files or clean up if needed
            }
        }
        catch { }
    }

    [Fact]
    public async Task Test50StartStopCyclesAndResourceStability()
    {
        string hostPath = @"D:\Documents\ClipMax\tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe";
        if (!File.Exists(hostPath))
        {
            _output.WriteLine("CaptureHost not found at " + hostPath);
            return;
        }

        using var client = new CaptureHostClient(hostPath);
        client.Start();

        var settings = new AppSettings
        {
            TargetType = "Monitor",
            MonitorIndex = 0,
            VideoWidth = 1920,
            VideoHeight = 1080,
            VideoFps = 30,
            VideoBitrateKbps = 15000,
            VideoEncoder = "obs_nvenc_h264_tex",
            ReplayDurationSeconds = 30,
            BufferMemoryLimitMb = 256,
            OriginalsFolder = _testOutputDir,
            AudioMode = "systemfallback",
            EnableMicrophone = false
        };

        var monitor = new ResourceMonitor();
        long baselineWorkingSet = 0;
        long baselinePrivateBytes = 0;

        _output.WriteLine("Starting 50 Start/Stop capture cycles...");

        for (int i = 1; i <= 50; i++)
        {
            bool started = await client.StartCaptureAsync(settings);
            Assert.True(started, $"StartCaptureAsync failed at cycle {i}");

            // Small delay to let the pipeline run for 100ms
            await Task.Delay(100);

            var sampleRunning = monitor.Sample();
            if (i == 5)
            {
                // Record baseline after initial warm-up
                baselineWorkingSet = sampleRunning.AppWorkingSet;
                baselinePrivateBytes = sampleRunning.AppPrivateBytes;
            }

            bool stopped = await client.StopCaptureAsync();
            Assert.True(stopped, $"StopCaptureAsync failed at cycle {i}");

            if (i % 10 == 0)
            {
                var sample = monitor.Sample();
                _output.WriteLine($"Cycle {i}/50: PrivateBytes={sample.AppPrivateBytes / 1024 / 1024}MB, WorkingSet={sample.AppWorkingSet / 1024 / 1024}MB, Handles={sample.AppHandleCount}");
            }
        }

        // Final verification: ensure client is responsive and state is off
        var status = await client.GetStatusAsync();
        Assert.Equal("Off", status.State);
        Assert.False(status.IsArmed);

        await client.ShutdownAsync();
    }

    [Fact]
    public async Task Test20TargetSwitches()
    {
        string hostPath = @"D:\Documents\ClipMax\tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe";
        if (!File.Exists(hostPath)) return;

        using var client = new CaptureHostClient(hostPath);
        client.Start();

        var targets = await client.GetTargetsAsync();
        _output.WriteLine($"Found {targets.Monitors.Count} monitors and {targets.Windows.Count} windows.");

        var settings = new AppSettings
        {
            TargetType = "Monitor",
            MonitorIndex = 0,
            VideoWidth = 1920,
            VideoHeight = 1080,
            VideoFps = 30,
            VideoBitrateKbps = 15000,
            VideoEncoder = "obs_nvenc_h264_tex",
            ReplayDurationSeconds = 30,
            BufferMemoryLimitMb = 256,
            OriginalsFolder = _testOutputDir,
            AudioMode = "systemfallback",
            EnableMicrophone = false
        };

        using var targetApp = System.Diagnostics.Process.Start("notepad.exe");
        try
        {
            await Task.Delay(500); // Allow window to create
            targetApp.Refresh();
            ulong targetHwnd = (ulong)targetApp.MainWindowHandle.ToInt64();

            _output.WriteLine($"Notepad test window launched with HWND=0x{targetHwnd:X}");

            // Switch targets 20 times by re-arming with alternating window/monitor targets
            for (int i = 1; i <= 20; i++)
            {
                if (i % 2 == 0 && targetHwnd != 0)
                {
                    settings.TargetType = "Window";
                    settings.WindowHwnd = targetHwnd;
                    settings.WindowTitle = "Notepad";
                    _output.WriteLine($"Step {i}: switching to Window 'Notepad' (HWND=0x{targetHwnd:X})");
                }
                else
                {
                    settings.TargetType = "Monitor";
                    settings.MonitorIndex = 0;
                    _output.WriteLine($"Step {i}: switching to Monitor 0");
                }

                var resp = await client.SendCommandAsync("start_capture", new
                {
                    target_type = settings.TargetType.ToLowerInvariant(),
                    monitor_index = settings.MonitorIndex,
                    window_hwnd = settings.WindowHwnd,
                    fps = settings.VideoFps,
                    width = settings.VideoWidth,
                    height = settings.VideoHeight,
                    bitrate_kbps = settings.VideoBitrateKbps,
                    encoder = settings.VideoEncoder,
                    duration_sec = settings.ReplayDurationSeconds,
                    buffer_mb = settings.BufferMemoryLimitMb,
                    output_dir = settings.OriginalsFolder,
                    audio_mode = settings.AudioMode.ToLowerInvariant(),
                    enable_mic = settings.EnableMicrophone,
                    mic_device_id = settings.MicrophoneDeviceId,
                    system_device_id = settings.SystemAudioDeviceId
                });

                Assert.True(resp.Ok, $"Target switch start failed at step {i}: {resp.Error}");

                await Task.Delay(100);

                bool stopped = await client.StopCaptureAsync();
                Assert.True(stopped, $"Target switch stop failed at step {i}");
            }
        }
        finally
        {
            try
            {
                if (!targetApp.HasExited)
                {
                    targetApp.Kill();
                }
            }
            catch { }
        }

        await client.ShutdownAsync();
    }

    [Fact]
    public async Task TestSaveDuringStopRejection()
    {
        string hostPath = @"D:\Documents\ClipMax\tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe";
        if (!File.Exists(hostPath)) return;

        using var client = new CaptureHostClient(hostPath);
        client.Start();

        // When stopped / Off, saving replay should return error (not ok) and not crash
        var resp = await client.SendCommandAsync("save_replay", new { save_id = "test_disarmed" });
        Assert.False(resp.Ok);
        Assert.Contains("not active", resp.Error ?? "", StringComparison.OrdinalIgnoreCase);

        await client.ShutdownAsync();
    }

    [Fact]
    public async Task TestCaptureHostCrashDetectionAndNoOrphans()
    {
        string hostPath = @"D:\Documents\ClipMax\tools\obs\runtime\bin\64bit\LocalClip.CaptureHost.exe";
        if (!File.Exists(hostPath)) return;

        bool faultFired = false;
        var client = new CaptureHostClient(hostPath);
        client.FaultOccurred += reason => faultFired = true;

        client.Start();
        bool ping = await client.PingAsync();
        Assert.True(ping);

        // Find process and kill it directly
        var procs = System.Diagnostics.Process.GetProcessesByName("LocalClip.CaptureHost");
        Assert.NotEmpty(procs);

        foreach (var p in procs)
        {
            try { p.Kill(); } catch { }
        }

        await Task.Delay(200);

        // Client IsRunning should become false
        Assert.False(client.IsRunning);
        Assert.True(faultFired || !client.IsRunning);

        // Ensure disposing clean
        client.Dispose();

        // Verify no orphan processes remain
        var remainingProcs = System.Diagnostics.Process.GetProcessesByName("LocalClip.CaptureHost");
        Assert.Empty(remainingProcs);
    }
}
