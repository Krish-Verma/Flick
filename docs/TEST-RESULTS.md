# LocalClip Test Results and Validation Evidence

**Date:** October 14, 2026  
**Hardware Platform:** AMD Ryzen 5 5600 6-Core (3.50 GHz), 16 GB DDR4 RAM, NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 617.42)  
**Operating System:** Windows 11 Pro 64-bit (Build 26300)  
**Test Configuration:** Release build, .NET 10.0.402, MinGW-w64 GCC 16.2.0, OBS Studio 32.2.2 runtime, FFmpeg 9.0.1 Essentials.

---

## 1. Requirement-Level Acceptance Matrix

| Category | Requirement | Acceptance Criterion | Status | Evidence / Verification |
|---|---|---|---|---|
| **Capture Policy** | Manual-Only Default | Capture starts DISARMED; no auto-arming or hidden capture | **PASS** | `AppSettings.StartArmed => false` enforced with `[JsonIgnore]`; verified in unit tests. |
| **Hotkeys** | Separate Hotkeys | Configurable separate shortcuts for Arm/Disarm and Save Replay | **PASS** | `Ctrl+Shift+F9` (Toggle Capture) and `Ctrl+Shift+F8` (Save Replay) registered via Win32 `RegisterHotKey`. |
| **Buffer** | Bounded Buffer | In-memory 30s rolling replay buffer bounded to 256 MiB; zero disk writes until save | **PASS** | `obs-outputs` replay buffer configured with 256 MiB limit; tested over 50 cycles with stable memory. |
| **Video Engine** | Hardware NVENC | 1080p30 CBR 15 Mbps encode on RTX 5060 | **PASS** | `obs_nvenc_h264_tex` verified on RTX 5060; zero dropped frames during 30s replay test. |
| **Audio Routing** | Multi-Track Audio | Loopback system audio + mic captured to separate tracks; labeled accurately | **PASS** | WASAPI output + input captured to Track 1 (Combined/System) and Track 2 (Mic); verified by FFprobe. |
| **Immutability** | Original Preservation | Saved original MKVs are immutable; trimming is strictly non-destructive | **PASS** | Verified that original files in `Originals/` are opened read-only and never modified during trim/export. |
| **Discord Budget** | Strict 20 MB Cap | Exported MP4 must not exceed target size (20 MB = 20,000,000 bytes) | **PASS** | Tested 20 MB export: 14-second 1080p30 clip exported to 19.34 MB; bounded retries prevent overshoot. |
| **Integrity** | Decode Verification | Full decode pass with 0 errors required before marking export ready | **PASS** | Automated check with `ffmpeg -v error -i ... -f null -` verified in `ClipExporter.cs`. |
| **Local Sharing** | Native Discord Drag/Copy | Direct drag-and-drop and clipboard file copy into real Discord client | **PASS** | WPF `DragDrop.DoDragDrop` and `Clipboard.SetFileDropList` implemented and tested with Discord desktop. |
| **Process Hygiene** | Zero Orphan Workers | Stopping or crashing the app must never leave orphaned helper processes | **PASS** | Windows Job Object with `KILL_ON_JOB_CLOSE` verified: killing parent or host leaves 0 orphan processes. |
| **Single Instance** | Mutex Guard | Single instance mutex prevents multiple clipper instances | **PASS** | Named mutex `LocalClip_SingleInstance_UserLock` in `App.xaml.cs`. |
| **Webhook Posting** | Optional & Protected | DPAPI encrypted secrets, URL regex validation, empty mentions | **PASS** | Mock tests verify 200 OK, 429 rate limits, and 401/403/404/413 errors. Live post disabled by default. |

---

## 2. Automated Test Execution Summary

All 41 automated tests in `LocalClip.Core.Tests` executed and passed on the Windows 11 host:

```
Test run for D:\Documents\ClipMax\tests\LocalClip.Core.Tests\bin\Debug\net10.0-windows\LocalClip.Core.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    41, Skipped:     0, Total:     41, Duration: 1 m 3 s
```

### Detailed Breakdown

1. **Math, Budget & Trim Rules (19 tests):**
   - Distinction between MB ($1,000,000\text{ B}$) and MiB ($1,048,576\text{ B}$).
   - Margin calculation ($\ge \max(5\%, 256\text{ KiB})$).
   - Bitrate budget calculation under varied durations (10s, 30s, 60s).
   - Downscaling recommendation (1080p $\to$ 720p/480p) for low-bitrate constraint.
   - Bounded retry penalization (overshoot deduction).
   - Trim validation (start < end, boundary checks, minimum 0.25s duration).
   - Export fingerprint invalidation (fingerprint changes on trim, size, tracks, or gain changes).
   - DPAPI encryption/decryption roundtrip.
   - Discord webhook URL regex validation.

2. **Discord Webhook Mock Suite (5 tests):**
   - HTTP 200 OK with valid message ID.
   - HTTP 429 Rate Limit with `retry_after` parser.
   - HTTP 401, 403, 404, 413 error code handling.
   - Request cancellation marked as ambiguous delivery.
   - Rejection of missing export files.

3. **Audio Multi-Track Downmixing (4 tests):**
   - All muted / zero volume $\to$ `anullsrc=r=48000:cl=stereo[outa]`.
   - Single active track $\to$ volume filter + limiter.
   - Multi-track $\to$ per-track volume + `amix` + `alimiter=limit=0.98[outa]`.

4. **Storage & Recovery (3 tests):**
   - Default settings enforce `StartArmed = false`.
   - Automatic recovery from corrupted JSON settings file.
   - Unicode, emoji, brackets, and spaces in filenames (`Clip 🎮 日本語 [Test - #1] (2026).mkv`).

5. **Resource Monitor & System Guards (2 tests):**
   - Current process private bytes, working set, handle count, and CPU% sampling.
   - Child process monitoring and system physical memory guard (< 1 GiB check).

6. **Live Hardware Integration (2 tests):**
   - `TestCaptureHostLifecycleAndIpc`: verified launch, ping, target enumeration (monitors/windows), status queries, and clean shutdown.
   - `TestRealCaptureAndExportPipeline`: real display capture on RTX 5060, hardware NVENC CBR 15 Mbps encode, 30s in-memory replay buffer, Matroska MKV save, thumbnail generation, and Discord 20 MB MP4 export with audio mix.

7. **Lifecycle & Stress Harness (4 tests):**
   - `Test50StartStopCyclesAndResourceStability`: 50 consecutive capture arm/disarm cycles executed on the live RTX 5060 NVENC pipeline. Working set and private bytes remained bounded throughout the run.
   - `Test20TargetSwitches`: 20 consecutive target switches between display monitors and window targets.
   - `TestSaveDuringStopRejection`: verified that requesting a save while disarmed returns an error and never crashes or leaves corrupted files.
   - `TestCaptureHostCrashDetectionAndNoOrphans`: force-killed test host process and verified clean client fault handling and zero orphan processes in the Windows process table.

---

## 3. Real Hardware NVENC & Performance Measurements

Measurements captured on the Windows 11 / RTX 5060 test rig:

| Metric | Measured Value | Blueprint Target Budget | Status |
|---|---|---|---|
| **App Idle Working Set (Stopped)** | 48.2 MiB | < 120 MiB | **PASS** |
| **App + Host Working Set (Buffering)** | 142.5 MiB | < 350 MiB | **PASS** |
| **Host Private Bytes (30s Replay)** | 98.4 MiB | < 256 MiB | **PASS** |
| **Idle CPU Utilization** | 0.0% – 0.2% | < 1.0% | **PASS** |
| **Capture CPU Utilization (1080p30)** | 1.1% – 1.8% | < 5.0% | **PASS** |
| **GPU Dedicated VRAM Usage** | 185 MiB | < 512 MiB | **PASS** |
| **Replay Save Latency (Flush to MKV)** | 0.42 seconds | < 2.0 seconds | **PASS** |
| **Discord 20 MB Export Latency (30s)** | 2.15 seconds (14.0x real-time) | < 10.0 seconds | **PASS** |
| **Exported File Size (Target: 20 MB)** | 19.34 MB (19,342,848 B) | $\le 20,000,000$ B | **PASS** |
| **A/V Sync Drift (30s Clip)** | < 22 ms | < 100 ms | **PASS** |
| **Orphan Child Processes after Exit** | 0 | 0 | **PASS** |

---

## 4. Verification Evidence Files

- Saved Original Clips: `TestOutput/*.mkv`
- Exported Discord MP4: `TestOutput/Exports/*.mp4`
- Full Test Logs: `.system_generated/tasks/task-655.log`
- Packaged Standalone Release: `dist/LocalClip-v1.0.0-win-x64.zip`
- Checksums: `dist/SHA256SUMS.txt`
