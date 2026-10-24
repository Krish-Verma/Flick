using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LocalClip.Core.Diagnostics;

namespace LocalClip.App.ViewModels;

public partial class MainViewModel
{
    private readonly Dispatcher _resourceDispatcher = Dispatcher.CurrentDispatcher;
    private Func<int?, CancellationToken, Task<ResourceSample>>? _resourceSampleAsync;
    private Func<int?>? _resourceHostIdProvider;
    private Task _resourceRefreshTask = Task.CompletedTask;
    private CancellationTokenSource? _resourceRefreshCancellation;
    private bool _resourceSamplingPaused;
    private bool _resourceSamplingDisposed;
    private bool _resourceUsageEnabled;
    private bool _resourceCpuWarmupRequired = true;
    private int _resourceSamplingGeneration;
    private long _resourceLastPublishedTimestamp;
    private int? _resourcePublishedHostId;
    private int _resourcePublishedCaptureEpoch;
    private string _resourceStatsDetails = "Waiting for a resource sample. Sampled about once per second. External export tools and the separate recording-test helper are excluded.";

    public string ResourceStatsDetails
    {
        get => _resourceStatsDetails;
        set
        {
            if (_resourceStatsDetails == value) return;
            _resourceStatsDetails = value;
            OnPropertyChanged();
        }
    }

    public bool ResourceStatsVisible => _resourceUsageEnabled;

    public void RefreshResourceUsagePreferences()
    {
        bool enabled = _settings.ShowResourceUsage;
        if (_resourceUsageEnabled == enabled) return;
        _resourceUsageEnabled = enabled;
        _resourceCpuWarmupRequired = true;
        _resourceSamplingGeneration++;
        _resourceLastPublishedTimestamp = 0;
        try { _resourceRefreshCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        int? hostId;
        try { hostId = _resourceHostIdProvider?.Invoke(); }
        catch { hostId = -1; }
        PublishResourceSample(new ResourceSample(), hostId);
        OnPropertyChanged(nameof(ResourceStatsVisible));
    }

    private void InitializeResourceSampling()
    {
        _resourceSampleAsync = _resourceMonitor.SampleAsync;
        _resourceHostIdProvider = () => _captureHost.HostProcessId;
        _resourceUsageEnabled = _settings.ShowResourceUsage;
        _resourceCpuWarmupRequired = true;
        PublishResourceSample(new ResourceSample(), null);
    }

    // This timer handler is independent of recording IPC and its busy guards.
    // A slow query stays owned, while old display values expire after 3 seconds.
    private void OnResourcePollTick(object? sender, EventArgs args)
    {
        if (_resourceSamplingDisposed || _resourceSamplingPaused || !_resourceUsageEnabled || _isPreparingToQuit) return;
        int? hostId;
        try { hostId = _resourceHostIdProvider?.Invoke(); }
        catch
        {
            PublishResourceSample(new ResourceSample(), _resourcePublishedHostId ?? -1);
            return;
        }
        if (_resourceLastPublishedTimestamp != 0 &&
            (_resourcePublishedHostId != hostId || _resourcePublishedCaptureEpoch != _captureEpoch ||
             Stopwatch.GetElapsedTime(_resourceLastPublishedTimestamp) >= TimeSpan.FromSeconds(3)))
            PublishResourceSample(new ResourceSample(), hostId);
        if (!_resourceRefreshTask.IsCompleted || _resourceSampleAsync is not { } sampler) return;

        int generation = _resourceSamplingGeneration, epoch = _captureEpoch;
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        _resourceRefreshCancellation = cancellation;
        _resourceRefreshTask = RefreshResourceStatsAsync(sampler, hostId, generation, epoch, cancellation);
    }

    private bool IsCurrentResourceObservation(int? hostId, int generation, int epoch)
    {
        if (_resourceSamplingDisposed || _resourceSamplingPaused || !_resourceUsageEnabled || _isPreparingToQuit ||
            generation != _resourceSamplingGeneration || epoch != _captureEpoch) return false;
        try { return hostId == _resourceHostIdProvider?.Invoke(); }
        catch { return false; }
    }

    private async Task RefreshResourceStatsAsync(Func<int?, CancellationToken, Task<ResourceSample>> sampler,
        int? hostId, int generation, int epoch, CancellationTokenSource cancellation)
    {
        try
        {
            // ResourceMonitor owns all process reads and settles them before
            // cancellation completes. This task is retained until that point.
            ResourceSample sample = await sampler(hostId, cancellation.Token).ConfigureAwait(false);
            if (cancellation.IsCancellationRequested || _resourceDispatcher.HasShutdownStarted) return;
            await _resourceDispatcher.InvokeAsync(() =>
            {
                if (cancellation.IsCancellationRequested || !IsCurrentResourceObservation(hostId, generation, epoch)) return;
                if (_resourceCpuWarmupRequired)
                {
                    // This read advances the monitor's CPU baselines past a
                    // pause. Display CPU only after the next fresh interval.
                    sample.HasAppCpuSample = false;
                    sample.HasCaptureHostCpuSample = false;
                    _resourceCpuWarmupRequired = false;
                }
                PublishResourceSample(sample, hostId);
                _resourcePublishedHostId = hostId;
                _resourcePublishedCaptureEpoch = epoch;
                _resourceLastPublishedTimestamp = Stopwatch.GetTimestamp();
                // An unknown system-memory query must never be interpreted as zero.
                if (sample.HasSystemSample && sample.IsLowSystemMemory)
                    StatusDetails = "Warning: System available physical memory < 1 GB!";
            }, DispatcherPriority.Background).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // A resource read is optional display information, not an engine fault.
            if (!_resourceDispatcher.HasShutdownStarted)
            {
                try
                {
                    await _resourceDispatcher.InvokeAsync(() =>
                    {
                        if (IsCurrentResourceObservation(hostId, generation, epoch))
                            PublishResourceSample(new ResourceSample(), hostId);
                    }, DispatcherPriority.Background).Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _resourceRefreshCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    // Pure presentation projection, also used by isolated rendering fixtures.
    private void PublishResourceSample(ResourceSample sample, int? hostId)
    {
        bool appMemory = sample.HasAppSample && sample.AppPrivateBytes >= 0;
        bool hostMemory = sample.HasCaptureHostSample && sample.CaptureHostPrivateBytes >= 0;
        bool appCpu = sample.HasAppCpuSample && double.IsFinite(sample.AppCpuPercent) && sample.AppCpuPercent >= 0;
        bool hostCpu = sample.HasCaptureHostCpuSample && double.IsFinite(sample.CaptureHostCpuPercent) && sample.CaptureHostCpuPercent >= 0;
        bool memoryAvailable = appMemory && (hostId == null || hostMemory);
        bool cpuAvailable = appCpu && (hostId == null || hostCpu);
        double memoryBytes = (double)sample.AppPrivateBytes + (hostId == null ? 0 : sample.CaptureHostPrivateBytes);
        double cpu = Math.Clamp(sample.AppCpuPercent + (hostId == null ? 0 : sample.CaptureHostCpuPercent), 0, 100);
        string memory = memoryAvailable ? FormatResourceMemory(memoryBytes) : "--";
        string cpuText = cpuAvailable ? cpu.ToString("F1", CultureInfo.CurrentCulture) + "%" : "--";
        ResourceStatsText = $"RAM {memory} · CPU {cpuText}";

        string AppMemory() => appMemory ? FormatResourceMemory(sample.AppPrivateBytes) : "--";
        string HostMemory() => hostMemory ? FormatResourceMemory(sample.CaptureHostPrivateBytes) : "--";
        string Cpu(bool available, double value) => available ? Math.Clamp(value, 0, 100).ToString("F1", CultureInfo.CurrentCulture) + "%" : "--";
        string recording = hostId == null ? "Recording: not running"
            : $"Recording: RAM {HostMemory()} · CPU {Cpu(hostCpu, sample.CaptureHostCpuPercent)}";
        ResourceStatsDetails = $"Flick: RAM {AppMemory()} · CPU {Cpu(appCpu, sample.AppCpuPercent)}\n{recording}\n" +
            "Sampled about once per second. RAM is private process memory; CPU is normalized across logical processors.\n" +
            "Totals include Flick and its current recording helper. External export tools and the separate recording-test helper are excluded. -- means no current sample is available.";
    }

    private static string FormatResourceMemory(double bytes)
    {
        double megabytes = bytes / (1024d * 1024d);
        return megabytes >= 1024
            ? (megabytes / 1024).ToString("F1", CultureInfo.CurrentCulture) + " GB"
            : megabytes.ToString("F0", CultureInfo.CurrentCulture) + " MB";
    }

    private void PauseResourceSampling()
    {
        _resourceSamplingPaused = true;
        _resourceSamplingGeneration++;
        try { _resourceRefreshCancellation?.Cancel(); }
        catch (ObjectDisposedException) { } // The owned read may have settled concurrently.
    }

    private async Task<bool> StopResourceSamplingAsync(TimeSpan? cleanupBudget = null)
    {
        PauseResourceSampling();
        try
        {
            await _resourceRefreshTask.WaitAsync(cleanupBudget ?? TimeSpan.FromSeconds(3));
            return true;
        }
        catch (TimeoutException) { return false; } // Retain the task; never start a competing read.
    }

    private void ResumeResourceSampling()
    {
        if (!_resourceSamplingDisposed)
        {
            _resourceCpuWarmupRequired = true;
            _resourceSamplingPaused = false;
        }
    }

    private void DisposeResourceSampling()
    {
        if (_resourceSamplingDisposed) return;
        _resourceSamplingDisposed = true;
        PauseResourceSampling();
        _pollTimer.Tick -= OnResourcePollTick;
    }
}
