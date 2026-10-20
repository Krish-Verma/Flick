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
        _testOutputDir = Path.Combine(
            Environment.GetEnvironmentVariable("FLICK_TEST_OUTPUT_DIR")
                ?? Path.Combine(Path.GetTempPath(), "Flick-HardwareTests"), "Lifecycle");
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

    private static string RequiredHostPath()
    {
        string path = Environment.GetEnvironmentVariable("FLICK_CAPTURE_HOST_PATH")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "tools", "obs", "runtime", "bin", "64bit", "LocalClip.CaptureHost.exe"));
        Assert.True(File.Exists(path), "Hardware validation enabled but capture host missing: " + path);
        return path;
    }

    [HardwareFact]
    public async Task Test50StartStopCyclesAndResourceStability()
    {
        string hostPath = RequiredHostPath();


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
        long baselineHostPrivateBytes = 0;
        long peakHostPrivateBytes = 0;

        _output.WriteLine("Starting 50 Start/Stop capture cycles...");

        for (int i = 1; i <= 50; i++)
        {
            bool started = await client.StartCaptureAsync(settings);
            Assert.True(started, $"StartCaptureAsync failed at cycle {i}");

            // Small delay to let the pipeline run for 100ms
            await Task.Delay(100);

            var runningStatus = await client.GetStatusAsync();
            peakHostPrivateBytes = Math.Max(peakHostPrivateBytes, runningStatus.PrivateBytes);
            if (i == 5)
                baselineHostPrivateBytes = runningStatus.PrivateBytes;

            bool stopped = await client.StopCaptureAsync();
            Assert.True(stopped, $"StopCaptureAsync failed at cycle {i}");

            if (i % 10 == 0)
            {
                var sample = monitor.Sample();
                _output.WriteLine($"Cycle {i}/50: PrivateBytes={sample.AppPrivateBytes / 1024 / 1024}MB, WorkingSet={sample.AppWorkingSet / 1024 / 1024}MB, Handles={sample.AppHandleCount}");
            }
        }

        Assert.True(peakHostPrivateBytes < 805_306_368L,
            $"Capture host exceeded 768 MiB private bytes: {peakHostPrivateBytes}");
        _output.WriteLine($"Host baseline={baselineHostPrivateBytes}, peak={peakHostPrivateBytes} private bytes");

        // Final verification: ensure client is responsive and state is off
        var status = await client.GetStatusAsync();
        Assert.Equal("Off", status.State);
        Assert.False(status.IsArmed);

        await client.ShutdownAsync();
    }

    [HardwareFact]
    public async Task Test20TargetSwitches()
    {
        string hostPath = RequiredHostPath();

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
            Assert.NotEqual(0UL, targetHwnd); // Never silently skip actual window capture.

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

    [HardwareFact]
    public async Task TestSaveDuringStopRejection()
    {
        string hostPath = RequiredHostPath();

        using var client = new CaptureHostClient(hostPath);
        client.Start();

        // When stopped / Off, saving replay should return error (not ok) and not crash
        var resp = await client.SendCommandAsync("save_replay", new { save_id = "test_disarmed" });
        Assert.False(resp.Ok);
        Assert.Contains("not active", resp.Error ?? "", StringComparison.OrdinalIgnoreCase);

        await client.ShutdownAsync();
    }

    [HardwareFact]
    public async Task TestCaptureHostCrashDetectionAndNoOrphans()
    {
        string hostPath = RequiredHostPath();

        bool faultFired = false;
        var client = new CaptureHostClient(hostPath);
        client.FaultOccurred += reason => faultFired = true;

        client.Start();
        bool ping = await client.PingAsync();
        Assert.True(ping);

        // Terminate ONLY the host launched by this specific test.
        int ownedHostId = client.HostProcessId ?? throw new InvalidOperationException("Test host was not started");
        using (var ownedProcess = System.Diagnostics.Process.GetProcessById(ownedHostId))
        {
            ownedProcess.Kill();
            Assert.True(ownedProcess.WaitForExit(5000), "Test-owned capture host failed to exit");
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
