using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using LocalClip.App.ViewModels;
using LocalClip.Core.Diagnostics;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Last assertion in the existing Application fixture. Owns fake resource reads, then disposes the VM; no process, settings file or capture is opened.</summary>
internal static class ResourceSamplingAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const long MiB = 1024L * 1024;

    public static void RunLastAndDispose(MainViewModel vm)
    {
        object? Invoke(string name, params object?[] arguments) => typeof(MainViewModel).GetMethod(name, Hidden)!.Invoke(vm, arguments);
        object? Get(string name) => typeof(MainViewModel).GetField(name, Hidden)!.GetValue(vm);
        void Set(string name, object? value) => typeof(MainViewModel).GetField(name, Hidden)!.SetValue(vm, value);
        void Tick() => Invoke("OnResourcePollTick", null, EventArgs.Empty);
        Task OwnedTask() => (Task)Get("_resourceRefreshTask")!;
        void Finish(PendingRead read, ResourceSample sample)
        {
            read.Completion.SetResult(sample);
            Pump(OwnedTask());
        }
        var reads = new List<PendingRead>();
        int? hostId = null;
        bool preference = vm.Settings.ShowResourceUsage;
        object? diagnosticBefore = Get("_isRecordingDiagnosticRunning"), automationBefore = Get("_gameAutomationBusy");
        string globalStatus = "Synthetic health status must survive unavailable resource samples.";
        try
        {
            Assert.True(OwnedTask().IsCompleted);
            // Exercise the same initialization path used by the constructor with
            // the saved preference off. It must create no optional read task.
            vm.Settings.ShowResourceUsage = false;
            Invoke("InitializeResourceSampling");
            Set("_resourceSampleAsync", new Func<int?, CancellationToken, Task<ResourceSample>>((pid, token) =>
            {
                var read = new PendingRead(pid, token);
                reads.Add(read);
                return read.Completion.Task; // Deliberately retain ownership after cancellation until explicitly settled.
            }));
            Set("_resourceHostIdProvider", new Func<int?>(() => hostId));
            Tick(); Tick(); Assert.Empty(reads); Assert.False(vm.ResourceStatsVisible);

            vm.Settings.ShowResourceUsage = true;
            vm.RefreshResourceUsagePreferences();
            Assert.True(vm.ResourceStatsVisible); Assert.Empty(reads);
            Assert.Equal("RAM -- · CPU --", vm.ResourceStatsText);
            vm.StatusDetails = globalStatus;
            Set("_isRecordingDiagnosticRunning", true); Set("_gameAutomationBusy", true);
            Tick(); Tick(); Tick();
            Assert.Single(reads); Assert.Null(reads[0].HostId);
            Finish(reads[0], Sample(64, 0, appCpuAvailable: false));
            Assert.Equal("RAM 64 MB · CPU --", vm.ResourceStatsText); // A missing first CPU baseline is not 0%.
            Assert.Equal(globalStatus, vm.StatusDetails); // No system-memory sample is not low memory.

            Tick(); Tick(); Assert.Equal(2, reads.Count);
            Finish(reads[1], Sample(96, 1.2));
            Assert.Equal($"RAM 96 MB · CPU {1.2.ToString("F1", CultureInfo.CurrentCulture)}%", vm.ResourceStatsText);
            Assert.Contains("Flick:", vm.ResourceStatsDetails); Assert.Contains("Recording: not running", vm.ResourceStatsDetails);
            Assert.Contains("External export tools", vm.ResourceStatsDetails);
            Set("_isRecordingDiagnosticRunning", false); Set("_gameAutomationBusy", false);

            // Turning the setting off cancels but retains an in-flight read;
            // its eventual result cannot repopulate the hidden indicator.
            Tick(); Assert.Equal(3, reads.Count);
            vm.Settings.ShowResourceUsage = false; vm.RefreshResourceUsagePreferences();
            Assert.False(vm.ResourceStatsVisible); Assert.True(reads[2].Token.IsCancellationRequested);
            Tick(); Assert.Equal(3, reads.Count);
            vm.Settings.ShowResourceUsage = true; vm.RefreshResourceUsagePreferences();
            Tick(); Assert.Equal(3, reads.Count); // Re-enable cannot overlap the canceled batch.
            Finish(reads[2], Sample(999, 99));
            Assert.Equal("RAM -- · CPU --", vm.ResourceStatsText);
            Assert.Equal(3, reads.Count); Assert.Equal("RAM -- · CPU --", vm.ResourceStatsText);
            Tick(); Finish(reads[3], Sample(128, 2));
            Assert.Equal("RAM 128 MB · CPU --", vm.ResourceStatsText); // Suppress the disabled-duration CPU average.
            Tick(); Finish(reads[4], Sample(128, 2));
            Assert.Equal($"RAM 128 MB · CPU {2d.ToString("F1", CultureInfo.CurrentCulture)}%", vm.ResourceStatsText);

            // An old host cannot publish after a switch, even if its read
            // finishes before the new host has had a chance to be sampled.
            hostId = 101; Tick(); Assert.Equal(6, reads.Count);
            Assert.Equal(101, reads[5].HostId); Assert.Equal("RAM -- · CPU --", vm.ResourceStatsText);
            hostId = 202; Tick(); Assert.Equal(6, reads.Count);
            Finish(reads[5], Sample(100, 1, 200, 2));
            Assert.Equal("RAM -- · CPU --", vm.ResourceStatsText);
            Tick(); Assert.Equal(202, reads[6].HostId);
            Finish(reads[6], Sample(128, 1, 256, 2));
            Assert.Equal($"RAM 384 MB · CPU {3d.ToString("F1", CultureInfo.CurrentCulture)}%", vm.ResourceStatsText);
            Assert.Contains("Flick: RAM 128 MB", vm.ResourceStatsDetails);
            Assert.Contains("Recording: RAM 256 MB", vm.ResourceStatsDetails);

            // A stalled batch cannot keep old numbers visible or spawn
            // overlapping work. A refused stop can resume after late settlement.
            Tick(); Assert.Equal(8, reads.Count);
            Set("_resourceLastPublishedTimestamp", Stopwatch.GetTimestamp() - 4 * Stopwatch.Frequency);
            Tick(); Assert.Equal(8, reads.Count); Assert.Equal("RAM -- · CPU --", vm.ResourceStatsText);
            var stopped = (Task<bool>)Invoke("StopResourceSamplingAsync", TimeSpan.FromMilliseconds(20))!;
            Pump(stopped); Assert.False(stopped.GetAwaiter().GetResult());
            Assert.True(reads[7].Token.IsCancellationRequested); Assert.False(OwnedTask().IsCompleted);
            Invoke("ResumeResourceSampling"); Tick(); Assert.Equal(8, reads.Count);
            Finish(reads[7], Sample(444, 44, 444, 44));
            Assert.Equal("RAM -- · CPU --", vm.ResourceStatsText);
            Tick(); Finish(reads[8], Sample(128, 1, 256, 2));
            Assert.Equal("RAM 384 MB · CPU --", vm.ResourceStatsText); // Late canceled work did not consume warm-up.
            Tick(); Finish(reads[9], Sample(128, 1, 256, 2));
            Assert.Equal($"RAM 384 MB · CPU {3d.ToString("F1", CultureInfo.CurrentCulture)}%", vm.ResourceStatsText);

            Tick(); reads[10].Completion.SetException(new IOException("Synthetic unavailable process metrics."));
            Pump(OwnedTask()); Assert.Equal("RAM -- · CPU --", vm.ResourceStatsText);
            Assert.Equal(globalStatus, vm.StatusDetails);
            Tick(); Finish(reads[11], Sample(128, 1, 256, 2));

            // Run through actual production Dispose while a fake reader is
            // still settling. Cancellation is not permission for late UI writes.
            Tick(); Assert.Equal(13, reads.Count);
            string beforeDispose = vm.ResourceStatsText;
            vm.Dispose(); Assert.True(reads[12].Token.IsCancellationRequested);
            Tick(); Assert.Equal(13, reads.Count);
            Finish(reads[12], Sample(777, 77, 777, 77));
            Assert.Equal(beforeDispose, vm.ResourceStatsText);
            Assert.True(OwnedTask().IsCompleted);
        }
        finally
        {
            vm.Settings.ShowResourceUsage = preference; // In-memory only, never Save.
            Set("_isRecordingDiagnosticRunning", diagnosticBefore); Set("_gameAutomationBusy", automationBefore);
            vm.Dispose();
            foreach (var read in reads) read.Completion.TrySetCanceled();
            Pump(OwnedTask());
        }
    }

    private static ResourceSample Sample(long appMb, double appCpu, long hostMb = 0, double hostCpu = 0, bool appCpuAvailable = true) => new()
    {
        HasAppSample = true, HasAppCpuSample = appCpuAvailable, AppPrivateBytes = appMb * MiB, AppCpuPercent = appCpu,
        HasCaptureHostSample = hostMb > 0, HasCaptureHostCpuSample = hostMb > 0,
        CaptureHostPrivateBytes = hostMb * MiB, CaptureHostCpuPercent = hostCpu
    };

    private static void Pump(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => frame.Continue = false;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        timeout.Start();
        try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); }
        finally { timeout.Stop(); }
        Assert.True(task.IsCompleted, "The owned resource sample did not settle within the test bound.");
        task.GetAwaiter().GetResult();
    }

    private sealed record PendingRead(int? HostId, CancellationToken Token)
    {
        public TaskCompletionSource<ResourceSample> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
