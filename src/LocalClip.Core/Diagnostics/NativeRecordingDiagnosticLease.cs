using System.Diagnostics;
using System.Runtime.CompilerServices;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;

[assembly: InternalsVisibleTo("LocalClip.Core.Tests")]

namespace LocalClip.Core.Diagnostics;

/// <summary>One consented test owns one helper; it can never reuse the application's replay helper.</summary>
public sealed class NativeRecordingDiagnosticLease : IRecordingDiagnosticLease
{
    private readonly object _gate = new();
    // Host teardown can synchronously emit a terminal event while holding its
    // own process lock. Callbacks must never acquire the lifecycle prefix gate.
    private readonly object _replayGate = new();
    private readonly INativeRecordingDiagnosticHost _host;
    private bool _started;
    private bool _closed;
    private Task<bool>? _cleanup;
    private TaskCompletionSource<RecordingDiagnosticReplay>? _replay;
    private string? _saveId;

    public NativeRecordingDiagnosticLease(string? hostExePath = null) : this(new NativeDiagnosticHost(hostExePath)) { }

    internal NativeRecordingDiagnosticLease(INativeRecordingDiagnosticHost host)
    {
        _host = host;
        _host.ReplaySaved += ReplaySaved;
        _host.SaveFailed += SaveFailed;
    }

    public async Task<CaptureEncodingProfile> StartAsync(RecordingDiagnosticPlan plan, string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var settings = plan.CreateCaptureSettings(outputDirectory);
        var target = plan.Target;
        var options = new CaptureStartOptions(plan.EnableSystemAudio,
            target.Kind == "Window" ? new(target.WindowHwnd, target.WindowPid, target.WindowTitle, target.WindowExecutable,
                target.WindowExecutablePath, target.WindowProcessCreationFileTime) : null,
            target.Kind == "Monitor" ? target.MonitorId : null);
        options.Validate(settings);
        using var cancellation = cancellationToken.Register(AbortOwnedHelper);
        try
        {
            Task<CaptureHostCapabilities> capabilitiesTask;
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_closed || _started) throw new InvalidOperationException("This recording test helper cannot be restarted.");
                _started = true;
                // Hold the prefix lock until process creation and the idle command
                // are issued. Cancel/dispose can never race a later helper spawn.
                _host.Start();
                capabilitiesTask = _host.GetCapabilitiesAsync();
            }
            var capabilities = await capabilitiesTask.ConfigureAwait(false);
            if (!capabilities.SupportsSafeDiagnostics)
                throw new InvalidOperationException("The capture helper cannot safely run this recording test. Update Flick and try again.");
            if (target.WindowProcessCreationFileTime != 0 && !capabilities.ExactWindowBinding)
                throw new InvalidOperationException("The capture helper cannot bind this consented process instance. Update Flick and try again.");
            Task<bool> start;
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_closed) throw new OperationCanceledException("The recording test was closed.");
                start = _host.StartCaptureAsync(settings, options);
            }
            if (!await start.ConfigureAwait(false))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(_host.LastStartError)
                    ? "The recording test could not start with the selected source and encoder." : _host.LastStartError);
            cancellationToken.ThrowIfCancellationRequested();
            return _host.Profile ?? throw new InvalidDataException("The helper did not confirm its actual recording profile.");
        }
        catch
        {
            AbortOwnedHelper();
            await WaitForExitAfterAbortAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    public async Task<CaptureHostStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var cancellation = cancellationToken.Register(AbortOwnedHelper);
        try
        {
            Task<CaptureHostStatus> status;
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_closed) throw new InvalidOperationException("The recording test helper is closed.");
                status = _host.GetStatusAsync();
            }
            var result = await status.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch
        {
            AbortOwnedHelper();
            await WaitForExitAfterAbortAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    public async Task<RecordingDiagnosticReplay> SaveAndWaitAsync(string saveId, CancellationToken cancellationToken)
    {
        using var cancellation = cancellationToken.Register(AbortOwnedHelper);
        TaskCompletionSource<RecordingDiagnosticReplay> replay;
        Task<bool> acknowledgement;
        try
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_closed || _saveId != null) throw new InvalidOperationException("The recording test cannot save another replay.");
                lock (_replayGate)
                {
                    _saveId = saveId;
                    _replay = replay = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                acknowledgement = _host.SaveReplayAsync(saveId);
            }
            try
            {
                bool accepted = await acknowledgement.ConfigureAwait(false);
                if (!accepted && !replay.Task.IsCompleted)
                    throw new InvalidOperationException("The capture helper refused the recording test save.");
            }
            catch (Exception) when (replay.Task.IsCompletedSuccessfully)
            {
                // A real matching terminal file outranks a missing or late ACK.
            }
            catch (TimeoutException) when (_host.HasPendingReplaySave && !cancellationToken.IsCancellationRequested)
            {
                // Missing ACK is not rejection: a matching terminal file can
                // still arrive within the runner's bounded save deadline.
            }
            var result = await replay.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch
        {
            AbortOwnedHelper();
            await WaitForExitAfterAbortAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    private void ReplaySaved(string saveId, string path, IReadOnlyList<string> tracks)
    {
        lock (_replayGate)
            if (saveId == _saveId)
                _replay?.TrySetResult(new(saveId, path, Array.AsReadOnly(tracks.ToArray())));
    }

    private void SaveFailed(string? saveId)
    {
        lock (_replayGate)
            if (saveId == null || saveId == _saveId)
                _replay?.TrySetException(new IOException("The recording test helper exited or could not finalize its replay."));
    }

    private void AbortOwnedHelper()
    {
        lock (_gate)
        {
            _closed = true;
            _host.Abort();
            lock (_replayGate) _replay?.TrySetException(new IOException("The recording test was stopped."));
        }
    }

    private async Task<bool> WaitForExitAfterAbortAsync()
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        return await _host.WaitForExitAsync(wait.Token).ConfigureAwait(false);
    }

    public Task<bool> StopAndConfirmExitAsync(CancellationToken cleanupToken)
    {
        lock (_gate)
        {
            _closed = true;
            return _cleanup ??= StopCoreAsync(cleanupToken);
        }
    }

    private async Task<bool> StopCoreAsync(CancellationToken cleanupToken)
    {
        using var cancellation = cleanupToken.Register(AbortOwnedHelper);
        try { await _host.ShutdownAsync().ConfigureAwait(false); }
        catch { /* Owned job cleanup below is required even if the protocol failed. */ }
        finally { AbortOwnedHelper(); }
        bool exited = await _host.WaitForExitAsync(cleanupToken).ConfigureAwait(false);
        if (exited)
        {
            _host.ReplaySaved -= ReplaySaved;
            _host.SaveFailed -= SaveFailed;
            _host.Dispose();
        }
        return exited;
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        await StopAndConfirmExitAsync(deadline.Token).ConfigureAwait(false);
    }
}

internal interface INativeRecordingDiagnosticHost : IDisposable
{
    event Action<string, string, IReadOnlyList<string>>? ReplaySaved;
    event Action<string?>? SaveFailed;
    CaptureEncodingProfile? Profile { get; }
    string LastStartError { get; }
    bool HasPendingReplaySave { get; }
    void Start();
    Task<CaptureHostCapabilities> GetCapabilitiesAsync();
    Task<bool> StartCaptureAsync(AppSettings settings, CaptureStartOptions options);
    Task<CaptureHostStatus> GetStatusAsync();
    Task<bool> SaveReplayAsync(string saveId);
    Task ShutdownAsync();
    void Abort();
    Task<bool> WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class NativeDiagnosticHost : INativeRecordingDiagnosticHost
{
    private readonly CaptureHostClient _client;
    private System.Diagnostics.Process? _exitObserver;
    public event Action<string, string, IReadOnlyList<string>>? ReplaySaved;
    public event Action<string?>? SaveFailed;
    public CaptureEncodingProfile? Profile => _client.CurrentProfile;
    public string LastStartError => _client.LastStartError;
    public bool HasPendingReplaySave => _client.HasPendingReplaySaves;

    public NativeDiagnosticHost(string? path)
    {
        _client = new(path);
        _client.ReplaySavedWithTracks += (id, file, tracks) => ReplaySaved?.Invoke(id, file, tracks);
        _client.ReplaySaveAborted += _ => SaveFailed?.Invoke(null);
        _client.FaultOccurred += _ => SaveFailed?.Invoke(null);
        _client.EventReceived += (name, data) =>
        {
            if (name == "save_failed" && data is { } value && value.TryGetProperty("save_id", out var id))
                SaveFailed?.Invoke(id.GetString());
        };
    }

    public void Start()
    {
        _client.Start();
        if (_client.HostProcessId is not int processId) throw new IOException("The recording test helper exited before initialization.");
        _exitObserver = System.Diagnostics.Process.GetProcessById(processId);
        _ = _exitObserver.Handle; // Retain this exact process handle across client/job disposal.
    }

    public Task<CaptureHostCapabilities> GetCapabilitiesAsync() => _client.GetCapabilitiesAsync();
    public Task<bool> StartCaptureAsync(AppSettings settings, CaptureStartOptions options) => _client.StartCaptureAsync(settings, options);
    public Task<CaptureHostStatus> GetStatusAsync() => _client.GetStatusAsync();
    public Task<bool> SaveReplayAsync(string saveId) => _client.SaveReplayAsync(saveId, "RecordingTest");
    public Task ShutdownAsync() => _client.ShutdownAsync();
    public void Abort() => _client.Dispose();

    public async Task<bool> WaitForExitAsync(CancellationToken cancellationToken)
    {
        if (_exitObserver == null) return _client.HostExitConfirmed;
        try
        {
            if (_exitObserver.HasExited) return true;
            await _exitObserver.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return _exitObserver.HasExited;
        }
        catch (OperationCanceledException) { return _exitObserver.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    public void Dispose() { _client.Dispose(); _exitObserver?.Dispose(); }
}
