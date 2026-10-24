using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using LocalClip.Core.Diagnostics;

namespace LocalClip.Core.Tests;

public class ResourceMonitorTests
{
    private const int AppId = 100;
    private const int HostId = 200;

    [Fact]
    public async Task FirstSampleProvidesMemoryButNeverInventsCpuUsage()
    {
        var reader = new FakeReader();
        reader.Processes[HostId] = Reading(HostId, cpuSeconds: 20);
        var monitor = Create(reader, new ManualClock());

        var sample = await monitor.SampleAsync(HostId);

        Assert.True(sample.HasAppSample);
        Assert.True(sample.HasCaptureHostSample);
        Assert.True(sample.HasSystemSample);
        Assert.False(sample.HasAppCpuSample);
        Assert.False(sample.HasCaptureHostCpuSample);
        Assert.Equal(2, sample.OwnedProcessCount);
        Assert.Equal(2048, sample.TotalAppOwnedPrivateBytes);
        Assert.False(sample.IsLowSystemMemory);
        Assert.All(reader.ProcessIds, id => Assert.Contains(id, new[] { AppId, HostId }));
    }

    [Fact]
    public async Task CpuUsesMonotonicIntervalsAndLogicalProcessorNormalization()
    {
        var reader = new FakeReader();
        var clock = new ManualClock();
        reader.Processes[HostId] = Reading(HostId, cpuSeconds: 5);
        var monitor = Create(reader, clock, processorCount: 4);
        await monitor.SampleAsync(HostId);

        clock.Advance(TimeSpan.FromSeconds(1));
        clock.UtcNow -= TimeSpan.FromDays(30); // Wall-clock corrections cannot inflate CPU.
        reader.Processes[AppId] = Reading(AppId, cpuSeconds: 10.8);
        reader.Processes[HostId] = Reading(HostId, cpuSeconds: 5.4);
        var sample = await monitor.SampleAsync(HostId);

        Assert.True(sample.HasAppCpuSample);
        Assert.True(sample.HasCaptureHostCpuSample);
        Assert.Equal(20, sample.AppCpuPercent, precision: 5);
        Assert.Equal(10, sample.CaptureHostCpuPercent, precision: 5);
        AssertFinite(sample);
    }

    [Fact]
    public async Task TooShortIntervalsAccumulateAgainstEarlierBaseline()
    {
        var reader = new FakeReader();
        var clock = new ManualClock();
        var monitor = Create(reader, clock);
        await monitor.SampleAsync();

        clock.Advance(TimeSpan.FromMilliseconds(50));
        reader.Processes[AppId] = Reading(AppId, cpuSeconds: 10.02);
        Assert.False((await monitor.SampleAsync()).HasAppCpuSample);

        clock.Advance(TimeSpan.FromMilliseconds(50));
        reader.Processes[AppId] = Reading(AppId, cpuSeconds: 10.04);
        var sample = await monitor.SampleAsync();
        Assert.True(sample.HasAppCpuSample);
        Assert.Equal(40, sample.AppCpuPercent, precision: 5);
    }

    [Fact]
    public async Task ZeroCpuDeltaIsAvailableOnlyAfterARealInterval()
    {
        var reader = new FakeReader();
        var clock = new ManualClock();
        var monitor = Create(reader, clock);
        await monitor.SampleAsync();
        Assert.False((await monitor.SampleAsync()).HasAppCpuSample);
        clock.Advance(TimeSpan.FromSeconds(1));

        var sample = await monitor.SampleAsync();
        Assert.True(sample.HasAppCpuSample);
        Assert.Equal(0, sample.AppCpuPercent);
    }

    [Theory]
    [InlineData(HostId, 2)]
    [InlineData(HostId + 1, 1)]
    public async Task HostIdentityChangeRequiresANewBaseline(int replacementId, long replacementStart)
    {
        var reader = new FakeReader();
        var clock = new ManualClock();
        reader.Processes[HostId] = Reading(HostId, cpuSeconds: 30);
        var monitor = Create(reader, clock);
        await monitor.SampleAsync(HostId);

        clock.Advance(TimeSpan.FromSeconds(1));
        reader.Processes[replacementId] = Reading(replacementId, replacementStart, 2);
        var replacement = await monitor.SampleAsync(replacementId);
        Assert.True(replacement.HasCaptureHostSample);
        Assert.False(replacement.HasCaptureHostCpuSample);
        Assert.True(replacement.HasAppCpuSample);

        clock.Advance(TimeSpan.FromSeconds(1));
        reader.Processes[replacementId] = Reading(replacementId, replacementStart, 2.25);
        var next = await monitor.SampleAsync(replacementId);
        Assert.True(next.HasCaptureHostCpuSample);
        Assert.Equal(25, next.CaptureHostCpuPercent, precision: 5);
    }

    [Fact]
    public async Task ExitedOrInaccessibleHostDoesNotRetainMemoryOrCpuBaseline()
    {
        var reader = new FakeReader();
        var clock = new ManualClock();
        reader.Processes[HostId] = Reading(HostId, cpuSeconds: 5);
        var monitor = Create(reader, clock);
        await monitor.SampleAsync(HostId);

        reader.Processes.TryRemove(HostId, out _);
        clock.Advance(TimeSpan.FromSeconds(1));
        var exited = await monitor.SampleAsync(HostId);
        Assert.False(exited.HasCaptureHostSample);
        Assert.False(exited.HasCaptureHostCpuSample);
        Assert.Equal(0, exited.CaptureHostPrivateBytes);
        Assert.Equal(1, exited.OwnedProcessCount);

        reader.Processes[HostId] = Reading(HostId, cpuSeconds: 6);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False((await monitor.SampleAsync(HostId)).HasCaptureHostCpuSample);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(AppId)]
    public async Task InvalidOrAppPidIsNeverDoubleCountedAsHost(int? hostId)
    {
        var reader = new FakeReader();
        var sample = await Create(reader, new ManualClock()).SampleAsync(hostId);

        Assert.Equal(1, sample.OwnedProcessCount);
        Assert.False(sample.HasCaptureHostSample);
        Assert.Equal(new[] { AppId }, reader.ProcessIds.ToArray());
    }

    [Fact]
    public async Task ReaderFailuresRemainUnavailableAndDoNotTriggerFalseMemoryWarnings()
    {
        var reader = new FakeReader
        {
            ProcessOverride = _ => throw new Win32Exception(5),
            MemoryOverride = () => throw new UnauthorizedAccessException()
        };
        var sample = await Create(reader, new ManualClock()).SampleAsync(HostId);

        Assert.False(sample.HasAppSample);
        Assert.False(sample.HasCaptureHostSample);
        Assert.False(sample.HasSystemSample);
        Assert.False(sample.HasAppCpuSample);
        Assert.False(sample.HasCaptureHostCpuSample);
        Assert.False(sample.IsLowSystemMemory);
        Assert.False(sample.IsHostMemoryExcessive);
        Assert.Equal(0, sample.OwnedProcessCount);
        AssertFinite(sample);
    }

    [Fact]
    public async Task OneFailedReaderDoesNotHideOtherAvailableReadings()
    {
        var reader = new FakeReader();
        reader.ProcessOverride = id => id == HostId
            ? throw new InvalidOperationException("The process has exited.")
            : Reading(id);
        reader.Memory = new(500, 2000);

        var sample = await Create(reader, new ManualClock()).SampleAsync(HostId);

        Assert.True(sample.HasAppSample);
        Assert.False(sample.HasCaptureHostSample);
        Assert.True(sample.HasSystemSample);
        Assert.True(sample.IsLowSystemMemory);
        Assert.Equal(1, sample.OwnedProcessCount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(101, 100)]
    public async Task InvalidSystemMemoryIsUnavailable(ulong available, ulong total)
    {
        var reader = new FakeReader { Memory = new(available, total) };
        var sample = await Create(reader, new ManualClock()).SampleAsync();

        Assert.False(sample.HasSystemSample);
        Assert.False(sample.IsLowSystemMemory);
    }

    [Fact]
    public async Task CpuRegressionAndBackwardTimestampResetRatherThanInventingPercentages()
    {
        var reader = new FakeReader();
        var clock = new ManualClock();
        var monitor = Create(reader, clock);
        await monitor.SampleAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        reader.Processes[AppId] = Reading(AppId, cpuSeconds: 9);
        Assert.False((await monitor.SampleAsync()).HasAppCpuSample);

        clock.Advance(TimeSpan.FromMilliseconds(-500));
        reader.Processes[AppId] = Reading(AppId, cpuSeconds: 9.5);
        Assert.False((await monitor.SampleAsync()).HasAppCpuSample);

        clock.Advance(TimeSpan.FromSeconds(1));
        reader.Processes[AppId] = Reading(AppId, cpuSeconds: 1000);
        var sample = await monitor.SampleAsync();
        Assert.True(sample.HasAppCpuSample);
        Assert.Equal(100, sample.AppCpuPercent);
        AssertFinite(sample);
    }

    [Fact]
    public async Task ConcurrentReadersAreOffContextAndBatchesCannotStackWorkers()
    {
        using var barrier = new ReadBarrier(3);
        var reader = new FakeReader { BeforeRead = barrier.Enter };
        reader.Processes[HostId] = Reading(HostId);
        var monitor = Create(reader, new ManualClock());
        var context = new ObservingContext();
        Task<ResourceSample> first;
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            first = monitor.SampleAsync(HostId);
        }
        finally { SynchronizationContext.SetSynchronizationContext(originalContext); }
        Task<ResourceSample>? second = null;
        try
        {
            await barrier.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = monitor.SampleAsync(HostId);
            Assert.Equal(3, reader.ReadCount);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(3, reader.MaximumActive);
            Assert.True(reader.AllReadersOnThreadPool);
        }
        finally
        {
            barrier.Release();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(6, reader.ReadCount);
        Assert.InRange(reader.MaximumActive, 1, 3);
        Assert.Equal(0, context.PostCount);
    }

    [Fact]
    public async Task PreCanceledSampleDoesNotReadOrChangeBaseline()
    {
        var reader = new FakeReader();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(reader, new ManualClock()).SampleAsync(HostId, canceled.Token));
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task WaitingCancellationDoesNotStartAnotherBatch()
    {
        using var barrier = new ReadBarrier(2);
        var reader = new FakeReader { BeforeRead = barrier.Enter };
        var monitor = Create(reader, new ManualClock());
        var first = monitor.SampleAsync();
        try
        {
            await barrier.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var canceled = new CancellationTokenSource();
            var waiting = monitor.SampleAsync(HostId, canceled.Token);
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(2, reader.ReadCount);
        }
        finally
        {
            barrier.Release();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task ActiveCancellationWaitsForReadersAndDoesNotPublishABaseline()
    {
        using var barrier = new ReadBarrier(2);
        var reader = new FakeReader { BeforeRead = barrier.Enter };
        var clock = new ManualClock();
        var monitor = Create(reader, clock);
        using var canceled = new CancellationTokenSource();
        var pending = monitor.SampleAsync(cancellationToken: canceled.Token);
        try
        {
            await barrier.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            canceled.Cancel();
            Assert.False(pending.IsCompleted); // A timeout must not abandon in-flight readers.
        }
        finally { barrier.Release(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, reader.Active);
        clock.Advance(TimeSpan.FromSeconds(1));
        var fresh = await monitor.SampleAsync();
        Assert.False(fresh.HasAppCpuSample);
    }

    [Fact]
    public void LegacySampleAcceptsADisposedProcessWithoutTakingItsOwnership()
    {
        var reader = new FakeReader();
        using var process = new System.Diagnostics.Process();
        process.Dispose();

        var sample = Create(reader, new ManualClock()).Sample(process);

        Assert.True(sample.HasAppSample);
        Assert.False(sample.HasCaptureHostSample);
        Assert.Equal(1, sample.OwnedProcessCount);
    }

    [Fact]
    public async Task RuntimeReaderSamplesOnlyCurrentProcessAndUnavailablePid()
    {
        var sample = await new ResourceMonitor().SampleAsync(int.MaxValue);

        Assert.True(sample.HasAppSample);
        Assert.True(sample.AppPrivateBytes > 0);
        Assert.True(sample.AppWorkingSet > 0);
        Assert.False(sample.HasAppCpuSample);
        Assert.False(sample.HasCaptureHostSample);
        Assert.Equal(1, sample.OwnedProcessCount);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(sample.AppHandleCount > 0);
            Assert.True(sample.HasSystemSample);
            Assert.True(sample.SystemTotalMemoryBytes > 0);
        }
        else
        {
            Assert.False(sample.HasSystemSample);
            Assert.False(sample.IsLowSystemMemory);
        }
    }

    private static ResourceMonitor Create(FakeReader reader, ManualClock clock, int processorCount = 1) =>
        new(reader, clock, AppId, processorCount);

    private static ProcessResourceReading Reading(int id, long start = 1, double cpuSeconds = 10) =>
        new(id, start, 1024, 2048, 10, TimeSpan.FromSeconds(cpuSeconds));

    private static void AssertFinite(ResourceSample sample)
    {
        Assert.True(double.IsFinite(sample.AppCpuPercent));
        Assert.True(double.IsFinite(sample.CaptureHostCpuPercent));
        Assert.InRange(sample.AppCpuPercent, 0, 100);
        Assert.InRange(sample.CaptureHostCpuPercent, 0, 100);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp = 1000;
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, (long)duration.TotalMilliseconds);
    }

    private sealed class FakeReader : IResourceReader
    {
        public ConcurrentDictionary<int, ProcessResourceReading> Processes { get; } = new();
        public ConcurrentQueue<int> ProcessIds { get; } = new();
        public Func<int, ProcessResourceReading?>? ProcessOverride { get; set; }
        public Func<SystemMemoryReading?>? MemoryOverride { get; set; }
        public Action? BeforeRead { get; set; }
        public SystemMemoryReading? Memory { get; set; } = new(4_000_000_000, 8_000_000_000);
        private int _readCount, _active, _maximumActive, _nonThreadPoolReads;
        public int ReadCount => Volatile.Read(ref _readCount);
        public int Active => Volatile.Read(ref _active);
        public int MaximumActive => Volatile.Read(ref _maximumActive);
        public bool AllReadersOnThreadPool => Volatile.Read(ref _nonThreadPoolReads) == 0;

        public FakeReader() => Processes[AppId] = Reading(AppId);

        public ProcessResourceReading? ReadProcess(int processId) => Read(() =>
        {
            ProcessIds.Enqueue(processId);
            return ProcessOverride is not null ? ProcessOverride(processId)
                : Processes.TryGetValue(processId, out var reading) ? reading : null;
        });

        public SystemMemoryReading? ReadSystemMemory() => Read(() =>
            MemoryOverride is not null ? MemoryOverride() : Memory);

        private T Read<T>(Func<T> action)
        {
            Interlocked.Increment(ref _readCount);
            int active = Interlocked.Increment(ref _active);
            int maximum;
            do { maximum = Volatile.Read(ref _maximumActive); }
            while (active > maximum && Interlocked.CompareExchange(ref _maximumActive, active, maximum) != maximum);
            if (!Thread.CurrentThread.IsThreadPoolThread) Interlocked.Increment(ref _nonThreadPoolReads);
            try { BeforeRead?.Invoke(); return action(); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    private sealed class ReadBarrier(int expected) : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private int _entered;
        public TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Enter()
        {
            if (Interlocked.Increment(ref _entered) == expected) AllEntered.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Reader fixture was not released.");
        }
        public void Release() => _release.Set();
        public void Dispose() => _release.Dispose();
    }

    private sealed class ObservingContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }
}
