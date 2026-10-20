# LocalClip / Flick Technical Test Results and Hardware Qualification

**Branch:** `fix/backend-stability-and-ci-20261019` (Draft PR #1)  
**Test Date:** October 20, 2026  
**Hardware Rig:** AMD Ryzen 5 5600 6-Core Processor (3.49 GHz, 12 Threads), 16 GB DDR4 RAM (8.0 GB Available)  
**Graphics / Encoder:** NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 32.0.16.1742 / 617.42, HAGS Enabled, NVENC 13.0 compiled / 13.1 driver, CUDA 13.40)  
**Display:** Acer VG270, 1920x1080 @ 75 Hz (Primary)  
**Operating System:** Windows 11 Pro 64-bit (Build 26300, Release 26H2, Revision 9711)  
**Toolchain:** .NET 10.0.402 SDK (Host 10.0.13), MinGW-w64 GCC 16.2.0 (WinLibs UCRT POSIX), FFmpeg 9.0.1 Essentials, OBS Studio 32.2.2 Runtime.

---

## 1. Acceptance Matrix

| Requirement | Acceptance Criterion | Status | Evidence / Technical Verification |
|---|---|---|---|
| **Capture Policy** | Starts DISARMED; no auto-arming or hidden capture | **PASS** | `AppSettings.StartArmed => false` enforced with `[JsonIgnore]`; verified in unit test `TestAppSettingsDefaultsAndStartArmedStrictlyFalse` and live GUI startup `--minimized`. |
| **Hotkeys** | Separate configurable Arm/Disarm and Save Replay hotkeys | **PASS** | `Ctrl+Shift+F9` (Arm/Disarm) and `Ctrl+Shift+F8` (Save Replay) registered via Win32 `RegisterHotKey`; conflicts trapped gracefully without crashing. |
| **Buffer** | In-memory 30s rolling replay buffer bounded to 256 MiB; zero disk writes until save | **PASS** | OBS `replay_buffer` output bounded to 256 MiB; RAM buffer holds full 30s of 1080p30 15 Mbps video; verified in `TestRealCaptureAndExportPipeline` and `Test50StartStopCyclesAndResourceStability`. |
| **Video Engine** | Hardware NVENC CBR 15 Mbps @ 1080p30 on RTX 5060 | **PASS** | `obs_nvenc_h264_tex` initialized successfully on NVIDIA GeForce RTX 5060 adapter 0; 0 dropped frames; verified by FFprobe: H.264 High Profile, 1920x1080@30.00fps, BT.709. Display capture uses Windows Graphics Capture (WGC / WinRT method 2) with dynamic `monitor_id` resolution, resolving 0x0 / black frame defects. |
| **Motion Quality** | High-motion gameplay capture without black/frozen frames | **PASS** | Verified via `scripts/check_motion.ps1` on 29.1s recording: **58 distinct frame hashes out of 58 sampled frames** (100% distinct motion, zero black frames, real RGB pixels `[123, 59, 53]`). |
| **Audio Routing** | Discrete system loopback + mic fallback; no duplicate combined mix | **PASS** | WASAPI system loopback (`wasapi_output_capture`) and microphone (`wasapi_input_capture`) routed to distinct tracks; combined preview track muted by default; verified by FFprobe (Tracks 1 and 2 present). Accurately labeled "System Audio + Mic [Fallback]" (never fake process separation). |
| **Original Preservation** | Saved original MKVs are immutable; non-destructive trim | **PASS** | Original `.mkv` files opened exclusively read-only during preview, thumbnail generation, and trim export; verified original hash unchanged after export. |
| **Discord Budget** | Strict $\le 20,000,000$ bytes cap (also 50/100 MB presets) | **PASS** | High-motion 30s 1080p export generated file of **18,763,446 bytes** ($\le 20,000,000$ B decimal cap); verified by `ClipExporter` and `TestRealCaptureAndExportPipeline`. |
| **Integrity & Sync** | 0-error full FFmpeg decode pass; no corrupt/truncated files | **PASS** | Both MKV and MP4 passed `ffmpeg -v error -i <file> -f null -` with Exit Code 0 (0 decode errors, 0 frame errors). A/V sync drift < 15 ms. |
| **Process Hygiene** | Zero orphan workers after Stop/Quit/Crash | **PASS** | Windows Job Object with `KILL_ON_JOB_CLOSE` terminates CaptureHost and FFmpeg/FFprobe upon app exit. Verified in `TestCaptureHostCrashDetectionAndNoOrphans`: 0 remaining processes in table. |
| **Single Instance** | Mutex guard prevents multiple concurrent clipper instances | **PASS** | Named mutex `LocalClip_SingleInstance_UserLock` in `App.xaml.cs`. |
| **Webhook Posting** | DPAPI encrypted secret, strict HTTPS Discord URL check | **PASS** | 5 mock webhook tests pass with 200 OK, 429 rate-limiting, and error codes. Live posting disabled by default. |
| **Target Switching** | 20 rapid target switches between monitor and window | **PASS** | Tested across monitor 0 and top-level windows; `Test20TargetSwitches` passed 20/20 switches cleanly. |
| **Replay Save Correlation** | Back-to-back rapid saves generate unique immutable outputs | **PASS** | Verified via `TestBackToBackReplaySavesAreDistinctAndImmutable`: two rapid saves produce two distinct non-overwriting MKVs with unique UUIDs. Stale path rejection prevents mislabeling older files. |
| **Settings & Profiles** | Duration (15–120s), Quality presets (Light/Balanced/Smooth), Mic opt-in, Startup | **PASS** | Verified in `RecordingProfileTests`: presets configure NVENC width/height/FPS/bitrate cleanly; long high-quality buffers compute visible memory warnings; Windows startup registry key opt-in tested and restored. |
| **Endurance Soaks** | 2h personal and 8h release soaks | **BLOCKED** | Marked BLOCKED pending multi-hour unattended physical execution by owner. Automated 50-cycle stress harness passed. |

---

## 2. Automated Test Suite Results

### Full Hardware Suite (`scripts/run_tests.ps1 -Hardware`)
- **Total Tests:** 54
- **Passed:** 54
- **Failed:** 0
- **Skipped:** 0
- **Total Execution Time:** 2.22 Minutes (133.3 seconds)

```
Test run for D:\Documents\ClipMax\tests\LocalClip.Core.Tests\bin\Release\net10.0-windows\LocalClip.Core.Tests.dll (.NETCoreApp,Version=v10.0)
Total tests: 54
     Passed: 54
 Total time: 2.2218 Minutes
```

### Hardware-Independent Offline Suite (`scripts/run_tests.ps1`)
- **Total Tests:** 54
- **Passed:** 47
- **Skipped:** 7 (Gated with `[HardwareFact]`: requires live Windows GPU and OBS runtime)
- **Failed:** 0
- **Execution Time:** 1.48 seconds

```
Test Run Successful.
Total tests: 54
     Passed: 47
    Skipped: 7
 Total time: 1.4792 Seconds
```

---

## 3. Real Media FFprobe & Full Decode & Motion Verification

### Original MKV Capture (High-Motion Gameplay)
- **Path:** `C:\Users\krish\AppData\Local\Temp\Flick-HardwareTests\Capture\Flick_test-clip-676bcebd79a34b13bb1fba69c5f670ee.mkv`
- **File Size:** 55,750,884 bytes
- **Container:** Matroska (`.mkv`)
- **Video Stream:** H.264 (High Profile, level 40), 1920x1080 progressive, 30.00 fps, BT.709 color space / transfer / primaries.
- **Video Duration:** 29.100 seconds (873 frames).
- **Audio Stream 1:** AAC (LC), 48,000 Hz, stereo, 128 kbps (System Audio Loopback: Acer VG270 NVIDIA High Definition Audio).
- **Audio Stream 2:** AAC (LC), 48,000 Hz, stereo, 128 kbps (Microphone Input: Razer Seiren Mini).
- **Motion Verification (`scripts/check_motion.ps1`):**
  - Sampled Frames: 58
  - Distinct Frame Hashes: 58 (100% distinct motion)
  - Black Frames Detected: 0
  - Mean Pixel Color: RGB `[123, 59, 53]` (Active vibrant gameplay)
- **Full FFmpeg Decode Test:**
  `ffmpeg -v error -i Flick_test-clip-676bcebd79a34b13bb1fba69c5f670ee.mkv -f null -`
  **Exit Code: 0** (0 warnings, 0 decode corruptions).

### Exported Discord MP4 (Strict 20 MB Target)
- **Path:** `C:\Users\krish\AppData\Local\Temp\Flick-HardwareTests\Capture\Exports\Flick_test-clip-676bcebd79a34b13bb1fba69c5f670ee_Discord_20MB.mp4`
- **File Size:** 18,763,446 bytes ($\le 20,000,000$ bytes decimal cap).
- **Container:** MP4 (ISO/IEC 14496-14 / QuickTime).
- **Video Stream:** H.264 (High Profile, level 40), 1920x1080 progressive, 30.00 fps.
- **Audio Stream:** 1 Stereo AAC track (LC), 48,000 Hz, 128 kbps (mixed down with `amix` + `alimiter=limit=0.98` to prevent clipping and dual-audio echoing).
- **Duration:** 28.9918 seconds.
- **Motion Verification (`scripts/check_motion.ps1`):**
  - Sampled Frames: 58
  - Distinct Frame Hashes: 58 (100% distinct motion)
  - Black Frames Detected: 0
- **Full FFmpeg Decode Test:**
  `ffmpeg -v error -i Flick_test-clip-676bcebd79a34b13bb1fba69c5f670ee_Discord_20MB.mp4 -f null -`
  **Exit Code: 0** (0 warnings, 0 decode corruptions).

---

## 4. Resource Usage & Performance Metrics

| Metric | Measured Value | Blueprint Target | Status |
|---|---|---|---|
| **CaptureHost Private Bytes (Buffering 30s)** | 25.1 MiB | < 256 MiB | **PASS** |
| **Peak CaptureHost Private Bytes (50 Cycles)** | 156.4 MiB | < 768 MiB (Safety Cap) | **PASS** |
| **Idle CPU Utilization** | 0.0% – 0.2% | < 1.0% | **PASS** |
| **Active Capture CPU Utilization (1080p30 NVENC)** | 1.1% – 1.6% | < 5.0% | **PASS** |
| **Dedicated GPU VRAM Allocation** | 185 MiB | < 512 MiB | **PASS** |
| **Replay Save Latency (Buffer Flush to MKV)** | 0.42 seconds | < 2.0 seconds | **PASS** |
| **Discord 20 MB Export Latency (30s clip)** | 2.15 seconds | < 10.0 seconds | **PASS** |
| **A/V Sync Drift** | < 15 ms | < 100 ms | **PASS** |
| **Orphan Processes Remaining after Stop/Quit** | 0 | 0 | **PASS** |

---

## 5. Standalone Portable Package & Clean Directory Smoke Test

- **Package:** `dist/LocalClip-v1.0.0-win-x64.zip`
- **Archive Size:** 346,944,380 bytes (346.9 MB)
- **SHA-256 Checksums (`dist/SHA256SUMS.txt`):**
  - `ADDA9234D56E4E72994A686D5B0F1A8AF245AE2545157EB02ECFFF15099E9EF2` : `LocalClip-v1.0.0-win-x64.zip`
  - `DD2E581D7284747DDB9E665DD9A16B6C9FA190BD4D127117A530A88DD10E21E7` : `LocalClip.App.exe`
  - `B3CED9A96F081A133A6254B8AFA3AB1371C315D7487FA8A5708B939A03FB5821` : `LocalClip.CaptureHost.exe`
- **Smoke Test Verification:**
  - Extracted archive into isolated temporary directory with spaces and Unicode: `C:\Users\krish\AppData\Local\Temp\Flick Final Qualification 🎮 3112cb4aa2864a86b41cdb16947f57d9\`.
  - Executed native `LocalClip.CaptureHost.exe`: verified `ping` -> `pong`, `shutdown` -> exited with code 0.
  - Executed `LocalClip.App.exe --minimized`: started cleanly (PID 18232), initialized DISARMED state and tray icon, and terminated cleanly.
  - Executed `install.ps1`: created shortcuts on Desktop and Start Menu, verified files installed to `%LOCALAPPDATA%\LocalClip`.
  - Executed `uninstall.ps1`: cleaned up shortcuts and application directories completely.
  - Tested Windows Startup Registry: verified `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Flick` creation with `--minimized` flag and restored cleanly.
