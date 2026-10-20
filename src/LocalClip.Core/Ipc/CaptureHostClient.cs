using System;
using System.Collections.Concurrent;
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
    private System.Diagnostics.Process? _process;
    private StreamWriter? _writer;
    private JobObject? _jobObject;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcResponse>> _pendingRequests = new();
    private CancellationTokenSource? _cts;
    private readonly string _hostExePath;
    private readonly string _workingDir;

    public event Action<string, JsonElement?>? EventReceived;
    public event Action<string>? StateChanged;
    public event Action<string, string>? ReplaySaved; // saveId, filePath
    public event Action<string>? FaultOccurred;

    private volatile bool _expectedShutdown;
    public bool IsRunning => _process != null && !_process.HasExited;
    public int? HostProcessId => IsRunning ? _process?.Id : null;

    public CaptureHostClient(string? hostExePath = null)
    {
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
        if (IsRunning) return;
        if (_process != null) Dispose();
        _expectedShutdown = false;

        if (!File.Exists(_hostExePath))
        {
            throw new FileNotFoundException($"CaptureHost executable not found at: {_hostExePath}");
        }

        _jobObject = new JobObject();
        _cts = new CancellationTokenSource();

        var psi = new ProcessStartInfo
        {
            FileName = _hostExePath,
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
        var stdoutThread = new Thread(ReadStdoutLoop) { IsBackground = true, Name = "CaptureHostStdoutReader" };
        stdoutThread.Start();

        // Background stderr drainer thread
        var stderrThread = new Thread(ReadStderrLoop) { IsBackground = true, Name = "CaptureHostStderrDrainer" };
        stderrThread.Start();
    }

    private void ReadStdoutLoop()
    {
        var reader = _process?.StandardOutput;
        if (reader == null) return;

        try
        {
            while (!_cts?.IsCancellationRequested == true && !reader.EndOfStream)
            {
                string? line = reader.ReadLine();
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
                            tcs.TrySetResult(resp);
                        }
                    }
                    else if (root.TryGetProperty("event", out var evProp))
                    {
                        string evName = evProp.GetString() ?? "";
                        JsonElement? evData = root.TryGetProperty("data", out var dataProp) ? dataProp.Clone() : null;

                        EventReceived?.Invoke(evName, evData);

                        if (evName == "state_changed" && evData.HasValue && evData.Value.TryGetProperty("state", out var stateProp))
                        {
                            StateChanged?.Invoke(stateProp.GetString() ?? "Unknown");
                        }
                        else if (evName == "replay_saved" && evData.HasValue)
                        {
                            string saveId = evData.Value.TryGetProperty("save_id", out var sid) ? sid.GetString() ?? "" : "";
                            string path = evData.Value.TryGetProperty("path", out var pth) ? pth.GetString() ?? "" : "";
                            ReplaySaved?.Invoke(saveId, path);
                        }
                        else if (evName == "fault" && evData.HasValue)
                        {
                            string reason = evData.Value.TryGetProperty("reason", out var rsn) ? rsn.GetString() ?? "Unknown fault" : "Unknown fault";
                            FaultOccurred?.Invoke(reason);
                        }
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
            if (!_expectedShutdown)
            {
                var error = new IOException("Capture host exited unexpectedly; pending requests aborted.");
                foreach (var entry in _pendingRequests)
                    if (_pendingRequests.TryRemove(entry.Key, out var request)) request.TrySetException(error);
                try { FaultOccurred?.Invoke(error.Message); } catch { }
            }
        }
    }

    private void ReadStderrLoop()
    {
        var reader = _process?.StandardError;
        if (reader == null) return;
        try
        {
            while (!_cts?.IsCancellationRequested == true && !reader.EndOfStream)
            {
                string? line = reader.ReadLine();
                // Logs are drained silently to avoid console flooding, but could be logged if needed
            }
        }
        catch { }
    }

    public async Task<IpcResponse> SendCommandAsync(string cmd, object? parameters = null, int timeoutMs = 8000)
    {
        if (!IsRunning)
        {
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

        string json = JsonSerializer.Serialize(req);
        lock (_writer!)
        {
            _writer.WriteLine(json);
            _writer.Flush();
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

    public async Task<bool> StartCaptureAsync(AppSettings settings)
    {
        var parameters = new
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
        };

        var resp = await SendCommandAsync("start_capture", parameters);
        return resp.Ok;
    }

    public async Task<bool> SaveReplayAsync(string saveId, string prefix = "Clip")
    {
        var parameters = new
        {
            save_id = saveId,
            prefix = prefix
        };

        var resp = await SendCommandAsync("save_replay", parameters);
        return resp.Ok;
    }

    public async Task<CaptureHostStatus> GetStatusAsync()
    {
        var resp = await SendCommandAsync("get_status", null, 3000);
        if (!resp.Ok || !resp.Result.HasValue)
        {
            return new CaptureHostStatus { State = "Off" };
        }

        var r = resp.Result.Value;
        return new CaptureHostStatus
        {
            State = r.TryGetProperty("state", out var s) ? s.GetString() ?? "Off" : "Off",
            BufferedSec = r.TryGetProperty("buffered_sec", out var bs) ? bs.GetDouble() : 0.0,
            MaxSec = r.TryGetProperty("max_sec", out var ms) ? ms.GetDouble() : 30.0,
            IsArmed = r.TryGetProperty("is_armed", out var ia) && ia.GetBoolean(),
            TargetType = r.TryGetProperty("target_type", out var tt) ? tt.GetString() ?? "" : "",
            TargetValid = !r.TryGetProperty("target_valid", out var tv) || tv.GetBoolean(),
            PrivateBytes = r.TryGetProperty("private_bytes", out var pb) ? pb.GetInt64() : 0,
            WorkingSet = r.TryGetProperty("working_set", out var ws) ? ws.GetInt64() : 0
        };
    }

    public async Task<bool> StopCaptureAsync()
    {
        if (!IsRunning) return true;
        try
        {
            var resp = await SendCommandAsync("stop_capture", null, 5000);
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
            await SendCommandAsync("shutdown", null, 3000);
        }
        catch { }

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.WaitForExit(2000);
            }
        }
        catch { }

        Dispose();
    }

    public void Dispose()
    {
        _expectedShutdown = true;
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
        _process?.Dispose();
        _process = null;
        _writer = null;
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}
