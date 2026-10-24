using System.ComponentModel;
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
    public double CaptureHostCpuPercent { get; set; }

    // Numeric fields retain their legacy defaults. Consumers must check availability
    // before displaying them: a missing baseline or inaccessible process is not 0% CPU.
    public bool HasAppSample { get; set; }
    public bool HasCaptureHostSample { get; set; }
    public bool HasAppCpuSample { get; set; }
    public bool HasCaptureHostCpuSample { get; set; }
    public bool HasSystemSample { get; set; }

    public long TotalAppOwnedPrivateBytes => AppPrivateBytes + CaptureHostPrivateBytes;
    public ulong SystemAvailableMemoryBytes { get; set; }
    public ulong SystemTotalMemoryBytes { get; set; }
    public int OwnedProcessCount { get; set; }

    public bool IsLowSystemMemory => HasSystemSample && SystemAvailableMemoryBytes < 1_073_741_824UL;
    public bool IsHostMemoryExcessive => HasCaptureHostSample && CaptureHostPrivateBytes > 805_306_368L;
}

public class ResourceMonitor
{
    private static readonly TimeSpan MinimumCpuInterval = TimeSpan.FromMilliseconds(100);
    private readonly SemaphoreSlim _samplingGate = new(1, 1);
    private readonly IResourceReader _reader;
    private readonly TimeProvider _clock;
    private readonly int _appProcessId;
    private readonly int _processorCount;
    private TimedProcessReading? _appBaseline;
    private TimedProcessReading? _hostBaseline;

    public ResourceMonitor() : this(new WindowsResourceReader(), TimeProvider.System,
        Environment.ProcessId, Environment.ProcessorCount) { }

    internal ResourceMonitor(IResourceReader reader, TimeProvider clock, int appProcessId, int processorCount)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appProcessId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);
        _reader = reader;
        _clock = clock;
        _appProcessId = appProcessId;
        _processorCount = processorCount;
    }

    /// <summary>
    /// Reads only the app, the specified host PID, and physical memory. Each batch
    /// runs its independent readers concurrently off the caller's context. Batches
    /// are serialized, so slow reads cannot accumulate additional reader workers.
    /// Cancellation waits for started readers to settle and never publishes a partial batch.
    /// </summary>
    public async Task<ResourceSample> SampleAsync(int? captureHostProcessId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _samplingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? hostId = captureHostProcessId is > 0 && captureHostProcessId != _appProcessId
                ? captureHostProcessId : null;
            var appTask = Task.Run(() => ReadProcess(_appProcessId));
            var hostTask = hostId.HasValue
                ? Task.Run(() => ReadProcess(hostId.Value))
                : Task.FromResult<TimedProcessReading?>(null);
            var memoryTask = Task.Run(ReadSystemMemory);
            await Task.WhenAll(appTask, hostTask, memoryTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var app = await appTask.ConfigureAwait(false);
            var host = await hostTask.ConfigureAwait(false);
            var memory = await memoryTask.ConfigureAwait(false);
            var sample = new ResourceSample { Timestamp = _clock.GetUtcNow().LocalDateTime };
            if (app is not null)
            {
                sample.HasAppSample = true;
                sample.AppPrivateBytes = app.Reading.PrivateBytes;
                sample.AppWorkingSet = app.Reading.WorkingSetBytes;
                sample.AppHandleCount = app.Reading.HandleCount;
                sample.OwnedProcessCount++;
            }
            if (host is not null)
            {
                sample.HasCaptureHostSample = true;
                sample.CaptureHostPrivateBytes = host.Reading.PrivateBytes;
                sample.CaptureHostWorkingSet = host.Reading.WorkingSetBytes;
                sample.OwnedProcessCount++;
            }
            sample.HasAppCpuSample = TryGetCpu(app, ref _appBaseline, out double appCpu);
            sample.AppCpuPercent = appCpu;
            sample.HasCaptureHostCpuSample = TryGetCpu(host, ref _hostBaseline, out double hostCpu);
            sample.CaptureHostCpuPercent = hostCpu;
            if (memory is not null)
            {
                sample.HasSystemSample = true;
                sample.SystemAvailableMemoryBytes = memory.AvailableBytes;
                sample.SystemTotalMemoryBytes = memory.TotalBytes;
            }
            return sample;
        }
        finally { _samplingGate.Release(); }
    }

    // Preserve the synchronous entry point for existing non-UI callers. The caller
    // retains ownership of its Process; each reader opens/disposes its own handle.
    public ResourceSample Sample(System.Diagnostics.Process? captureHostProcess = null)
    {
        int? hostId = null;
        if (captureHostProcess is not null)
        {
            try { hostId = captureHostProcess.Id; }
            catch (InvalidOperationException) { }
        }
        return SampleAsync(hostId).GetAwaiter().GetResult();
    }

    private TimedProcessReading? ReadProcess(int processId)
    {
        try
        {
            var reading = _reader.ReadProcess(processId);
            if (reading is null || reading.ProcessId != processId || reading.StartTimeUtcTicks <= 0 ||
                reading.PrivateBytes < 0 || reading.WorkingSetBytes < 0 || reading.HandleCount < 0 ||
                reading.TotalCpuTime < TimeSpan.Zero) return null;
            return new TimedProcessReading(reading, _clock.GetTimestamp());
        }
        catch (Exception exception) when (IsUnavailable(exception)) { return null; }
    }

    private SystemMemoryReading? ReadSystemMemory()
    {
        try
        {
            var memory = _reader.ReadSystemMemory();
            return memory is { TotalBytes: > 0 } && memory.AvailableBytes <= memory.TotalBytes ? memory : null;
        }
        catch (Exception exception) when (IsUnavailable(exception)) { return null; }
    }

    private bool TryGetCpu(TimedProcessReading? current, ref TimedProcessReading? baseline, out double percent)
    {
        percent = 0;
        if (current is null) { baseline = null; return false; }
        var previous = baseline;
        if (previous is null || previous.Reading.ProcessId != current.Reading.ProcessId ||
            previous.Reading.StartTimeUtcTicks != current.Reading.StartTimeUtcTicks ||
            current.Timestamp <= previous.Timestamp || current.Reading.TotalCpuTime < previous.Reading.TotalCpuTime)
        {
            baseline = current;
            return false;
        }
        var elapsed = _clock.GetElapsedTime(previous.Timestamp, current.Timestamp);
        if (elapsed < MinimumCpuInterval) return false; // Keep the earlier baseline for a useful interval.
        baseline = current;
        double calculated = (current.Reading.TotalCpuTime - previous.Reading.TotalCpuTime).TotalSeconds /
            elapsed.TotalSeconds / _processorCount * 100;
        if (!double.IsFinite(calculated)) return false;
        percent = Math.Clamp(calculated, 0, 100);
        return true;
    }

    private static bool IsUnavailable(Exception exception) => exception is
        InvalidOperationException or ArgumentException or Win32Exception or NotSupportedException or
        UnauthorizedAccessException or IOException;

    private sealed record TimedProcessReading(ProcessResourceReading Reading, long Timestamp);
}

internal sealed record ProcessResourceReading(int ProcessId, long StartTimeUtcTicks, long PrivateBytes,
    long WorkingSetBytes, int HandleCount, TimeSpan TotalCpuTime);
internal sealed record SystemMemoryReading(ulong AvailableBytes, ulong TotalBytes);
internal interface IResourceReader
{
    ProcessResourceReading? ReadProcess(int processId);
    SystemMemoryReading? ReadSystemMemory();
}

internal sealed class WindowsResourceReader : IResourceReader
{
    public ProcessResourceReading? ReadProcess(int processId)
    {
        using var current = System.Diagnostics.Process.GetProcessById(processId);
        current.Refresh();
        if (current.HasExited) return null;
        long started = current.StartTime.ToUniversalTime().Ticks;
        long privateBytes = current.PrivateMemorySize64;
        long workingSet = current.WorkingSet64;
        int handles = current.HandleCount;
        var cpu = current.TotalProcessorTime;
        return current.HasExited ? null : new(processId, started, privateBytes, workingSet, handles, cpu);
    }

    public SystemMemoryReading? ReadSystemMemory()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref memory) ? new(memory.ullAvailPhys, memory.ullTotalPhys) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
