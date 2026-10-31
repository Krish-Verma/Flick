using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Models;
using LocalClip.Core.Process;

namespace LocalClip.Core.Ipc;

public class CaptureHostClient : IDisposable
{
    private readonly object _lifecycleGate = new();
    private System.Diagnostics.Process? _process;
    private StreamWriter? _writer;
    private JobObject? _jobObject;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcResponse>> _pendingRequests = new();
    private readonly PendingReplaySaves _pendingSaves = new();
    private CancellationTokenSource? _cts;
    private readonly string _hostExePath;
    private readonly string _workingDir;
    private readonly string _hostArguments;
    private CaptureHostCapabilities? _capabilities;
    private volatile System.Diagnostics.Process? _respondedProcess;

    public event Action<string, JsonElement?>? EventReceived;
    public event Action<string>? StateChanged;
    public event Action<string, string>? ReplaySaved; // saveId, filePath
    public event Action<string, string, IReadOnlyList<string>>? ReplaySavedWithTracks;
    public event Action<string>? FaultOccurred;
    public event Action<CaptureEncodingProfile>? CaptureConfigured;
    public CaptureEncodingProfile? CurrentProfile { get; private set; }
    public string LastStartError { get; private set; } = "";
    public string LastSaveError { get; private set; } = "";
    public bool HasPendingReplaySaves => _pendingSaves.Count != 0;
    public event Action? PendingReplaySavesChanged;
    public event Action<string>? ReplaySaveAborted;

    private volatile bool _expectedShutdown;
    private volatile bool _hostExitConfirmed = true;
    public bool IsRunning
    {
        get
        {
            var process = _process;
            try { return process != null && !process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }
    public int? HostProcessId => IsRunning ? _process?.Id : null;
    // A live process alone is not evidence that its IPC is responsive. Reset
    // this evidence whenever the owned helper is replaced or disposed.
    public bool HasResponded
    {
        get
        {
            var process = _process;
            if (process == null || !ReferenceEquals(_respondedProcess, process)) return false;
            try { return !process.HasExited && ReferenceEquals(_process, process); }
            catch (InvalidOperationException) { return false; }
        }
    }
    internal bool HostExitConfirmed => _hostExitConfirmed;

    public CaptureHostClient(string? hostExePath = null) : this(hostExePath, "") { }

    // Allows the no-GPU pipe fixture to use the installed script interpreter.
    // Product callers retain the argument-free native executable constructor.
    internal CaptureHostClient(string? hostExePath, string hostArguments)
    {
        _hostArguments = hostArguments;
        if (string.IsNullOrWhiteSpace(hostExePath))
        {
            string baseDir = AppContext.BaseDirectory;
            string? overridePath = Environment.GetEnvironmentVariable("FLICK_CAPTURE_HOST_PATH");
            string[] candidates = new[]
            {
                overridePath ?? "",
                Path.Combine(baseDir, "tools", "obs", "runtime", "bin", "64bit", "LocalClip.CaptureHost.exe"),
                Path.Combine(baseDir, "LocalClip.CaptureHost.exe"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..",
                    "tools", "obs", "runtime", "bin", "64bit", "LocalClip.CaptureHost.exe"))
            };
            _hostExePath = Array.Find(candidates, File.Exists) ?? candidates[1];
        }
        else
        {
            _hostExePath = hostExePath;
        }
        _workingDir = Path.GetDirectoryName(_hostExePath) ?? AppContext.BaseDirectory;
    }

    public void Start()
    {
        lock (_lifecycleGate) StartCore();
    }

    private void StartCore()
    {
        if (IsRunning) return;
        if (_process != null) Dispose();
        if (!_hostExitConfirmed)
            throw new InvalidOperationException("The previous capture helper has not confirmed its exit.");
        _expectedShutdown = false;
        _capabilities = null;
        _respondedProcess = null;

        if (!File.Exists(_hostExePath))
        {
            throw new FileNotFoundException($"CaptureHost executable not found at: {_hostExePath}");
        }

        _jobObject = new JobObject();
        _cts = new CancellationTokenSource();

        var psi = new ProcessStartInfo
        {
            FileName = _hostExePath,
            Arguments = _hostArguments,
            WorkingDirectory = _workingDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            _process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch CaptureHost");
            _hostExitConfirmed = false;
            if (!_jobObject.AssignProcess(_process))
            {
                try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException("Failed to confine CaptureHost to Flick's Windows Job Object");
            }
            _writer = _process.StandardInput;
        }
        catch
        {
            Dispose();
            throw;
        }

        // Background stdout reader thread
        var process = _process!;
        var stdout = process.StandardOutput;
        var stderr = process.StandardError;
        var token = _cts!.Token;
        var stdoutThread = new Thread(() => ReadStdoutLoop(process, stdout, token)) { IsBackground = true, Name = "CaptureHostStdoutReader" };
        stdoutThread.Start();

        // Background stderr drainer thread
        var stderrThread = new Thread(() => ReadStderrLoop(process, stderr, token)) { IsBackground = true, Name = "CaptureHostStderrDrainer" };
        stderrThread.Start();
    }

    private void ReadStdoutLoop(System.Diagnostics.Process process, StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !reader.EndOfStream)
            {
                string? line = reader.ReadLine();
                if (!ReferenceEquals(_process, process)) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("id", out var idProp) && !string.IsNullOrEmpty(idProp.GetString()))
                    {
                        string id = idProp.GetString()!;
                        bool ok = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
                        string? err = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
                        JsonElement? res = root.TryGetProperty("result", out var resProp) ? resProp.Clone() : null;

                        var resp = new IpcResponse
                        {
                            Id = id,
                            Ok = ok,
                            Result = res,
                            Error = err
                        };

                        if (_pendingRequests.TryRemove(id, out var tcs))
                        {
                            _respondedProcess = process;
                            tcs.TrySetResult(resp);
                        }
                    }
                    else if (root.TryGetProperty("event", out var evProp))
                    {
                        string evName = evProp.GetString() ?? "";
                        JsonElement? evData = root.TryGetProperty("data", out var dataProp) ? dataProp.Clone() : null;
                        ProcessIncomingEvent(evName, evData);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CaptureHost IPC Parse Error]: {ex.Message} on line: {line}");
                }
            }
        }
        catch { }
        finally
        {
            // EOF can precede OS process termination by a few milliseconds. This
            // reader is off the UI thread and belongs to exactly one helper.
            try { process.WaitForExit(2500); } catch { }
            if (ReferenceEquals(_process, process)) ConfirmSaveHostExit(process);
            if (ReferenceEquals(_process, process) && !_expectedShutdown)
            {
                var error = new IOException("Capture host exited unexpectedly; pending requests aborted.");
                foreach (var entry in _pendingRequests)
                    if (_pendingRequests.TryRemove(entry.Key, out var request)) request.TrySetException(error);
                try { FaultOccurred?.Invoke(error.Message); } catch { }
            }
        }
    }

    private void ProcessIncomingEvent(string eventName, JsonElement? data)
    {
        if (eventName is "replay_saved" or "save_failed")
        {
            if (data is not JsonElement terminal || terminal.ValueKind != JsonValueKind.Object ||
                !terminal.TryGetProperty("save_id", out var identity) || identity.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(identity.GetString())) return;

            string saveId = identity.GetString()!;
            string path = "";
            var names = new List<string>();
            if (eventName == "replay_saved")
            {
                if (!terminal.TryGetProperty("path", out var savedPath) || savedPath.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(savedPath.GetString())) return;
                path = savedPath.GetString()!;
                if (terminal.TryGetProperty("tracks", out var tracks))
                {
                    if (tracks.ValueKind != JsonValueKind.Array) return;
                    foreach (var track in tracks.EnumerateArray())
                    {
                        if (track.ValueKind != JsonValueKind.String) return;
                        names.Add(track.GetString()!);
                    }
                }
            }

            // Drop stale/duplicate terminal events before any subscriber can index
            // a clip or change health. A claim still counts as a pending save.
            if (!_pendingSaves.TryClaimCompletion(saveId)) return;
            try
            {
                NotifySubscribers(EventReceived, handler => handler(eventName, data));
                if (eventName == "replay_saved")
                {
                    IReadOnlyList<string> trackNames = names.AsReadOnly();
                    NotifySubscribers(ReplaySavedWithTracks, handler => handler(saveId, path, trackNames));
                    NotifySubscribers(ReplaySaved, handler => handler(saveId, path));
                }
            }
            finally
            {
                // Indexing subscribers establish their own work guard synchronously
                // before the native-save protection can be released.
                if (_pendingSaves.FinishCompletion(saveId))
                    NotifySubscribers(PendingReplaySavesChanged, handler => handler());
            }
            return;
        }

        if (eventName == "capture_configured" && CaptureEncodingProfile.TryParse(data, out var configured))
        {
            CurrentProfile = configured;
            NotifySubscribers(CaptureConfigured, handler => handler(configured!));
        }
        NotifySubscribers(EventReceived, handler => handler(eventName, data));

        if (eventName == "state_changed" && data is JsonElement stateData &&
            stateData.ValueKind == JsonValueKind.Object && stateData.TryGetProperty("state", out var state))
        {
            NotifySubscribers(StateChanged, handler => handler(state.GetString() ?? "Unknown"));
        }
        else if (eventName == "fault" && data is JsonElement fault && fault.ValueKind == JsonValueKind.Object)
        {
            string reason = fault.TryGetProperty("reason", out var value) ? value.GetString() ?? "Unknown fault" : "Unknown fault";
            NotifySubscribers(FaultOccurred, handler => handler(reason));
        }
    }

    private static void NotifySubscribers<THandler>(THandler? subscribers, Action<THandler> notify) where THandler : Delegate
    {
        if (subscribers == null) return;
        foreach (THandler subscriber in subscribers.GetInvocationList())
        {
            try { notify(subscriber); }
            catch (Exception ex) { Debug.WriteLine($"[CaptureHost subscriber failure]: {ex.GetType().Name}"); }
        }
    }

    private void ReadStderrLoop(System.Diagnostics.Process process, StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !reader.EndOfStream)
            {
                string? line = reader.ReadLine();
                if (!ReferenceEquals(_process, process)) break;
                if (!string.IsNullOrWhiteSpace(line)) { Console.WriteLine("[CaptureHost] " + line); }
            }
        }
        catch { }
    }

    public Task<IpcResponse> SendCommandAsync(string cmd, object? parameters = null, int timeoutMs = 8000) =>
        SendCommandCoreAsync(cmd, parameters, timeoutMs, expectedHelper: null);

    private async Task<IpcResponse> SendCommandCoreAsync(string cmd, object? parameters, int timeoutMs,
        System.Diagnostics.Process? expectedHelper)
    {
        if (expectedHelper != null)
        {
            // The OS may terminate a helper without acquiring _lifecycleGate.
            // Never turn a verified start into an automatic replacement start.
            if (!ReferenceEquals(_process, expectedHelper) || expectedHelper.HasExited)
                throw new IOException("The verified capture helper exited or changed before recording could start. Try starting replay again.");
        }
        else if (!IsRunning)
        {
            if (cmd is "get_status" or "save_replay" or "stop_capture" or "shutdown")
                throw new InvalidOperationException("The capture helper is no longer running.");
            Start();
        }

        string id = Guid.NewGuid().ToString("N");
        var req = new IpcRequest
        {
            Id = id,
            Cmd = cmd,
            Params = parameters
        };

        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        try
        {
            string json = JsonSerializer.Serialize(req);
            var writer = (expectedHelper?.StandardInput ?? _writer)
                ?? throw new IOException("Capture helper input is unavailable.");
            lock (writer)
            {
                writer.WriteLine(json);
                writer.Flush();
            }
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        using var reg = cts.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(id, out var pending))
            {
                pending.TrySetException(new TimeoutException($"IPC Command '{cmd}' (ID {id}) timed out after {timeoutMs}ms"));
            }
        });

        return await tcs.Task;
    }

    public async Task<bool> PingAsync()
    {
        var resp = await SendCommandAsync("ping");
        return resp.Ok;
    }

    public async Task<TargetList> GetTargetsAsync()
    {
        var resp = await SendCommandAsync("enum_targets");
        if (!resp.Ok || !resp.Result.HasValue)
        {
            throw new InvalidOperationException(resp.Error ?? "Failed to enumerate capture targets");
        }

        var result = new TargetList();
        var jsonEl = resp.Result.Value;

        if (jsonEl.TryGetProperty("windows", out var winsEl))
        {
            foreach (var w in winsEl.EnumerateArray())
            {
                result.Windows.Add(new TargetWindow
                {
                    Hwnd = w.GetProperty("hwnd").GetUInt64(),
                    Title = w.GetProperty("title").GetString() ?? "",
                    Pid = w.GetProperty("pid").GetUInt32(),
                    Exe = w.GetProperty("exe").GetString() ?? "",
                    ExecutablePath = w.TryGetProperty("executable_path", out var path) || w.TryGetProperty("exe_path", out path) ? path.GetString() ?? "" : "",
                    ProcessCreationFileTime = w.TryGetProperty("process_creation_time", out var created) ? created.GetUInt64() : 0,
                    Width = w.GetProperty("width").GetInt32(),
                    Height = w.GetProperty("height").GetInt32()
                });
            }
        }

        if (jsonEl.TryGetProperty("monitors", out var monsEl))
        {
            foreach (var m in monsEl.EnumerateArray())
            {
                result.Monitors.Add(new TargetMonitor
                {
                    Index = m.GetProperty("index").GetInt32(),
                    Device = m.GetProperty("device").GetString() ?? "",
                    Width = m.GetProperty("width").GetInt32(),
                    Height = m.GetProperty("height").GetInt32(),
                    Primary = m.GetProperty("primary").GetBoolean()
                });
            }
        }

        return result;
    }

    public static object CreateStartParameters(AppSettings settings, CaptureStartOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        options ??= new();
        options.Validate(settings);
        var values = new Dictionary<string, object>
        {
            ["target_type"] = settings.TargetType.ToLowerInvariant(), ["monitor_index"] = settings.MonitorIndex,
            ["window_hwnd"] = settings.WindowHwnd, ["fps"] = settings.VideoFps, ["width"] = settings.VideoWidth,
            ["height"] = settings.VideoHeight, ["bitrate_kbps"] = settings.VideoBitrateKbps,
            ["encoder"] = settings.VideoEncoder, ["allow_software_fallback"] = settings.AllowSoftwareFallback,
            ["duration_sec"] = settings.ReplayDurationSeconds, ["buffer_mb"] = settings.BufferMemoryLimitMb,
            ["output_dir"] = settings.OriginalsFolder, ["audio_mode"] = settings.AudioMode.ToLowerInvariant(),
            ["enable_mic"] = settings.EnableMicrophone, ["mic_device_id"] = settings.MicrophoneDeviceId,
            ["system_device_id"] = settings.SystemAudioDeviceId, ["enable_system_audio"] = options.EnableSystemAudio
        };
        if (options.EnableSystemAudio)
        {
            LocalClip.Core.Audio.AudioCapturePolicy.Validate(settings);
            if (LocalClip.Core.Audio.AudioCapturePolicy.IsSeparated(settings))
            {
                if (settings.GameAudioSource is { } game) values["game_audio"] = new { pid = game.ProcessId, creation_time = game.CreationTime, path = game.ExecutablePath };
                if (settings.VoiceAudioSource is { } voice) values["voice_audio"] = new { pid = voice.ProcessId, creation_time = voice.CreationTime, path = voice.ExecutablePath };
            }
        }
        if (options.ExpectedWindow is { } window)
        {
            values["expected_window_pid"] = window.ProcessId;
            values["expected_window_title"] = window.Title;
            values["expected_window_exe"] = window.Executable;
            if (window.ExecutablePath.Length != 0 || window.ProcessCreationFileTime != 0)
            {
                values["expected_window_path"] = window.ExecutablePath;
                values["expected_window_creation_time"] = window.ProcessCreationFileTime;
            }
        }
        if (options.ExpectedMonitorId != null) values["monitor_id"] = options.ExpectedMonitorId;
        return values;
    }

    public async Task<CaptureHostCapabilities> GetCapabilitiesAsync()
    {
        Task<IpcResponse> reply;
        System.Diagnostics.Process? helper;
        lock (_lifecycleGate)
        {
            if (!IsRunning) StartCore();
            helper = _process;
            reply = SendCommandAsync("get_capabilities");
        }
        var capabilities = CaptureHostCapabilities.FromResponse(await reply.ConfigureAwait(false));
        lock (_lifecycleGate)
        {
            if (!ReferenceEquals(helper, _process) || !IsRunning)
                throw new IOException("The recording test helper changed during capability verification.");
            _capabilities = capabilities;
        }
        return capabilities;
    }

    public async Task<bool> StartCaptureAsync(AppSettings settings, CaptureStartOptions? options = null)
    {
        if ((options?.EnableSystemAudio ?? true) && LocalClip.Core.Audio.AudioCapturePolicy.IsSeparated(settings))
        {
            LocalClip.Core.Audio.AudioCapturePolicy.Validate(settings);
            var capabilities = await GetCapabilitiesAsync().ConfigureAwait(false);
            if (!capabilities.ExactProcessAudio) throw new InvalidOperationException("This Windows/helper version does not support exact app audio. Select System Audio explicitly or update Flick.");
        }
        CurrentProfile = null;
        LastStartError = "";
        // Hardware discovery can try multiple vendor plugins before bounded CPU fallback.
        IpcResponse response;
        try
        {
            Task<IpcResponse> reply;
            lock (_lifecycleGate)
            {
                if (options?.RequiresCapabilities == true)
                {
                    if (!IsRunning || _capabilities == null)
                        throw new InvalidOperationException(options.RequiresDiagnosticCapabilities
                            ? "Verify capture helper capabilities before starting a consented recording test."
                            : "Verify capture helper capabilities before starting identity-bound capture.");
                    if (options.RequiresDiagnosticCapabilities && !_capabilities.SupportsSafeDiagnostics)
                        throw new InvalidOperationException("Verify capture helper capabilities before starting a consented recording test.");
                    if (options.RequiresSourceIdentityBinding && !_capabilities.SourceIdentityBinding)
                        throw new InvalidOperationException("Update the capture helper before recording this exact source.");
                }
                reply = SendCommandCoreAsync("start_capture", CreateStartParameters(settings, options), 60000,
                    options?.RequiresCapabilities == true ? _process : null);
            }
            response = await reply.ConfigureAwait(false);
        }
        catch
        {
            // A timed-out native startup must not later begin an unacknowledged
            // capture. Closing the confined helper also ends pending attempts.
            Dispose();
            throw;
        }
        var result = CaptureStartResult.FromResponse(response);
        LastStartError = result.Error;
        if (result.Profile != null)
        {
            CurrentProfile = result.Profile;
            CaptureConfigured?.Invoke(result.Profile);
        }
        if (!result.Success) CurrentProfile = null;
        return result.Success;
    }

    public async Task<bool> SaveReplayAsync(string saveId, string prefix = "Clip")
    {
        LastSaveError = "";
        if (!IsRunning)
        {
            ConfirmSaveHostExit(_process);
            LastSaveError = "Replay capture is no longer active.";
            return false;
        }
        _pendingSaves.Begin(saveId);
        PendingReplaySavesChanged?.Invoke();
        var parameters = new
        {
            save_id = saveId,
            prefix = prefix
        };

        try
        {
            var resp = await SendCommandAsync("save_replay", parameters);
            LastSaveError = resp.Ok ? "" : resp.Error ?? "Replay save was not accepted.";
            if (_pendingSaves.Acknowledge(saveId, resp.Ok)) PendingReplaySavesChanged?.Invoke();
            return resp.Ok;
        }
        catch
        {
            // A lost acknowledgement does not prove rejection. Keep this ID
            // pending until a terminal event or confirmed helper exit arrives.
            ConfirmSaveHostExit(_process);
            throw;
        }
    }

    public async Task<CaptureHostStatus> GetStatusAsync()
    {
        var resp = await SendCommandAsync("get_status", null, 3000);
        var status = CaptureHostStatus.FromResponse(resp);
        CurrentProfile = status.Profile;
        return status;
    }

    public async Task<bool> StopCaptureAsync()
    {
        if (!IsRunning) return true;
        try
        {
            var resp = await SendCommandAsync("stop_capture", null, 30000);
            return resp.Ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task ShutdownAsync()
    {
        if (!IsRunning) { Dispose(); return; }
        _expectedShutdown = true;
        try
        {
            await SendCommandAsync("shutdown", null, 30000);
        }
        catch { }

        try
        {
            if (_process != null && !_process.HasExited)
            {
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(2500));
            }
        }
        catch { }

        // Forced confinement cleanup can wait briefly for process termination.
        // Keep the dispatcher free to consume terminal save notifications.
        await Task.Run(Dispose);
    }

    public void Dispose()
    {
        lock (_lifecycleGate) DisposeCore();
    }

    private void DisposeCore()
    {
        _expectedShutdown = true;
        CurrentProfile = null;
        _capabilities = null;
        _respondedProcess = null;
        _cts?.Cancel();
        foreach (var entry in _pendingRequests)
            if (_pendingRequests.TryRemove(entry.Key, out var request))
                request.TrySetException(new ObjectDisposedException(nameof(CaptureHostClient)));
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch { }

        _jobObject?.Dispose();
        _jobObject = null;
        try { if (_process != null && !_process.HasExited) _process.WaitForExit(2500); } catch { }
        ConfirmSaveHostExit(_process);
        _process?.Dispose();
        _process = null;
        _writer = null;
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }

    private void ConfirmSaveHostExit(System.Diagnostics.Process? process)
    {
        bool exited = _hostExitConfirmed;
        try { if (process != null) exited = process.HasExited; }
        catch (InvalidOperationException) { exited = false; }
        if (exited) _hostExitConfirmed = true;
        var abandoned = _pendingSaves.HostExited(exited);
        if (abandoned.Count == 0) return;
        PendingReplaySavesChanged?.Invoke();
        ReplaySaveAborted?.Invoke("The capture helper exited before a pending replay save completed. Check your clips before retrying.");
    }
}
