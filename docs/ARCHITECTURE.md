# LocalClip Technical Architecture and Design Specification

## 1. System Overview

LocalClip is built as a modular, decoupled architecture consisting of a lightweight WPF/C# frontend orchestrator (`LocalClip.App` and `LocalClip.Core`) and an out-of-process native C++ capture engine (`LocalClip.CaptureHost.exe`) utilizing the proven OBS Studio (`libobs`) multimedia pipeline.

```
+-------------------------------------------------------------------------+
|                           LocalClip.App (WPF)                           |
|  - MainWindow (MVVM, MediaElement, Trimmer, Audio Mixer, Gallery)       |
|  - HotkeyManager (Win32 RegisterHotKey, conflict detection)             |
|  - Drag-Drop Handler (native DropList, Copy File clipboard)             |
+------------------------------------+------------------------------------+
                                     |
                                     v
+------------------------------------+------------------------------------+
|                          LocalClip.Core (C#)                            |
|  - CaptureHostClient (Async stdio JSON IPC protocol coordinator)        |
|  - JobObject (Win32 Job Object confinement: KILL_ON_JOB_CLOSE)          |
|  - ClipStorage (Atomic metadata store, Recycle Bin SHFileOperation)     |
|  - ClipExporter (Bitrate calculator, FFmpeg NVENC MP4, size validator)  |
|  - MediaProber (ffprobe JSON metadata, ffmpeg JPEG thumbnails)          |
|  - ResourceMonitor (Private bytes, handles, CPU%, memory guard)         |
|  - DiscordWebhookClient (DPAPI encrypted secret, rate limit 429)        |
+------------------------------------+------------------------------------+
                                     | Stdio IPC (JSON Lines)
                                     v
+------------------------------------+------------------------------------+
|                    LocalClip.CaptureHost.exe (C++)                      |
|  - libobs engine integration (D3D11 graphics, audio subsystems)         |
|  - Video Sources: win-capture (Graphics Capture / DShow / Desktop)      |
|  - Audio Sources: win-wasapi (Loopback & Microphone)                    |
|  - Hardware Encoder: obs-nvenc (obs_nvenc_h264_tex, CBR 15 Mbps)        |
|  - Output: replay_buffer (in-memory circular buffer <= 256 MiB)        |
|  - Matroska Muxer: ffmpeg_muxer (multi-track stereo AAC)                |
+-------------------------------------------------------------------------+
```

---

## 2. IPC Protocol Specification

Communication between `LocalClip.App` and `LocalClip.CaptureHost.exe` takes place exclusively over anonymous standard input/output (`stdin`/`stdout`) using UTF-8 JSON lines. Standard error (`stderr`) is reserved for redirected libobs log messages, ensuring stdout is never polluted.

### Request Format
```json
{
  "id": "e6a4b18c-32b0-4a88-82b5-31a81dc37841",
  "cmd": "start_capture",
  "params": {
    "target_type": "monitor",
    "monitor_index": 0,
    "window_hwnd": 0,
    "fps": 30,
    "width": 1920,
    "height": 1080,
    "bitrate_kbps": 15000,
    "encoder": "obs_nvenc_h264_tex",
    "duration_sec": 30,
    "buffer_mb": 256,
    "output_dir": "D:\\Documents\\ClipMax\\Clips\\Originals",
    "audio_mode": "systemfallback",
    "enable_mic": true,
    "mic_device_id": "default",
    "system_device_id": "default"
  }
}
```

### Response Format
```json
{
  "id": "e6a4b18c-32b0-4a88-82b5-31a81dc37841",
  "ok": true,
  "result": {
    "status": "Buffering"
  }
}
```

### Async Notification Event Format
```json
{
  "event": "replay_saved",
  "data": {
    "save_id": "clip_20261014_130000",
    "path": "D:\\Documents\\ClipMax\\Clips\\Originals\\Clip_2026-10-14_13-00-00.mkv"
  }
}
```

### Supported Commands
- `ping`: Liveness check. Returns `"pong"`.
- `enum_targets`: Enumerates all active top-level windows and connected display monitors.
- `start_capture`: Configures video/audio sources and encoders, arms capture, and starts the rolling in-memory replay buffer.
- `save_replay`: Flushes the buffer to an immutable `.mkv` file.
- `stop_capture`: Stops recording and disarms the buffer without shutting down the host.
- `get_status`: Returns current buffer state, buffered duration, private bytes, and working set.
- `shutdown`: Flushes pending operations, shuts down libobs, and cleanly exits the process.

---

## 3. Worker Confinement & Lifetime Management

To eliminate orphaned helper processes (a critical failure mode of older clippers), `LocalClip.Core.Process.JobObject` wraps the Windows Job Object API:
- The Job Object is configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` via `SetInformationJobObject`.
- The child `LocalClip.CaptureHost.exe` process is explicitly assigned to this job upon creation.
- If the host application terminates for any reason (normal quit, user taskkill, unexpected crash, unhandled exception), the Windows OS kernel instantly and unconditionally terminates all child processes in the job object. Zero orphan processes remain.

---

## 4. Size-Capped Discord Export Engine

The Discord export pipeline guarantees that exported MP4 files strictly comply with target size limits (default 20 MB = 20,000,000 bytes):

1. **MB vs MiB Distinction:**
   - 1 MB = $1,000,000$ bytes (Discord API upload limit)
   - 1 MiB = $1,048,576$ bytes (Binary RAM / buffer limit)
2. **Bitrate Budget Calculation:**
   $$\text{Margin} = \max(0.05 \times \text{TargetBytes}, 256\text{ KiB})$$
   $$\text{AvailableBytes} = \text{TargetBytes} - \text{Margin} - \text{PreviousOvershoot}$$
   $$\text{TotalBitrate} = \frac{\text{AvailableBytes} \times 8}{\text{DurationSeconds}}$$
   $$\text{VideoBitrate} = \text{TotalBitrate} - \text{AudioBitrate}$$
3. **Audio Multi-Track Downmixing Filtergraph:**
   - All muted / zero volume $\to$ `anullsrc=r=48000:cl=stereo[outa]`
   - Single track $\to$ `[0:a:X]volume=G.GG,alimiter=limit=0.98[outa]`
   - Multi-track $\to$ `[0:a:0]volume=1.00[a0];[0:a:1]volume=1.20[a1];[a0][a1]amix=inputs=2:normalize=0,alimiter=limit=0.98[outa]`
4. **Three-Tier Verification Gate:**
   - **Size Check:** File size $\le$ Target Bytes. If oversized, file deleted and budget recalculated with penalty.
   - **Stream Check:** FFprobe validates video stream, audio stream, duration, and metadata.
   - **Decode Integrity Check:** `ffmpeg -v error -i <file> -f null -` validates zero decoding errors or truncated GOPs.
5. **Atomic File Promotion:** The export is initially encoded to a unique `.tmp.mp4` path. Only upon passing all three verification tiers is it atomically moved to the final destination.

---

## 5. Security & Privacy Guarantees

- **100% Offline Core:** LocalClip requires zero network connectivity. All video processing, indexing, trimming, and playback occur entirely on the local machine.
- **DPAPI Secret Protection:** Optional Discord webhook URLs are protected using Windows Data Protection API (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`). The plain text token is never stored in settings or configuration files.
- **Empty Mentions Safeguard:** Webhook payloads explicitly specify `allowed_mentions: { parse: [] }`, preventing accidental mass `@everyone`, `@here`, or role pings.
- **Immutable Originals:** The original recorded MKV files are never modified, trimmed in-place, or overwritten.
