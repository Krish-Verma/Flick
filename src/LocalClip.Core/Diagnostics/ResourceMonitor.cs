using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LocalClip.Core.Diagnostics;

public class ResourceSample
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public long AppPrivateBytes { get; set; }
    public long AppWorkingSet { get; set; }
    public int AppHandleCount { get; set; }
    public double AppCpuPercent { get; set; }

    public long CaptureHostPrivateBytes { get; set; }
    public long CaptureHostWorkingSet { get; set; }

    public long TotalAppOwnedPrivateBytes => AppPrivateBytes + CaptureHostPrivateBytes;

    public ulong SystemAvailableMemoryBytes { get; set; }
    public ulong SystemTotalMemoryBytes { get; set; }

    public int OwnedProcessCount { get; set; }

    public bool IsLowSystemMemory => SystemAvailableMemoryBytes < 1_073_741_824UL; // < 1 GiB
    public bool IsHostMemoryExcessive => CaptureHostPrivateBytes > 805_306_368L;   // > 768 MiB
}

public class ResourceMonitor
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private DateTime _lastCpuSampleTime = DateTime.UtcNow;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;

    public ResourceSample Sample(System.Diagnostics.Process? captureHostProcess = null)
    {
        var sample = new ResourceSample();

        // App Process Metrics
        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            current.Refresh();
            sample.AppPrivateBytes = current.PrivateMemorySize64;
            sample.AppWorkingSet = current.WorkingSet64;
            sample.AppHandleCount = current.HandleCount;

            var now = DateTime.UtcNow;
            var cpuTime = current.TotalProcessorTime;
            double elapsedMs = (now - _lastCpuSampleTime).TotalMilliseconds;
            if (elapsedMs > 100)
            {
                double cpuMs = (cpuTime - _lastTotalProcessorTime).TotalMilliseconds;
                sample.AppCpuPercent = Math.Clamp((cpuMs / (elapsedMs * Environment.ProcessorCount)) * 100.0, 0.0, 100.0);
                _lastCpuSampleTime = now;
                _lastTotalProcessorTime = cpuTime;
            }
        }
        catch { }

        int procCount = 1;

        // Capture Host Process Metrics
        if (captureHostProcess != null && !captureHostProcess.HasExited)
        {
            try
            {
                captureHostProcess.Refresh();
                sample.CaptureHostPrivateBytes = captureHostProcess.PrivateMemorySize64;
                sample.CaptureHostWorkingSet = captureHostProcess.WorkingSet64;
                procCount++;
            }
            catch { }
        }

        sample.OwnedProcessCount = procCount;

        // System Physical Memory Metrics
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            sample.SystemTotalMemoryBytes = memStatus.ullTotalPhys;
            sample.SystemAvailableMemoryBytes = memStatus.ullAvailPhys;
        }

        return sample;
    }
}
