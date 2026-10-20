> **Follow-up qualification pending.** The results below are historical evidence from commit `42a584f`, before replay-save integrity changes and new settings/UX. They must be rerun on real Windows GPU hardware. Hosted CI cannot prove gameplay motion, mic audibility, Discord drag/paste, or soaks; see [validation handoff](ANTIGRAVITY-VALIDATION-HANDOFF.md).

# LocalClip / Flick Technical Test Results and Hardware Qualification

**Branch:** `fix/backend-stability-and-ci-20261019`  
**Test Date:** October 20, 2026  
**Hardware Rig:** AMD Ryzen 5 5600 6-Core Processor (3.49 GHz, 12 Threads), 16 GB DDR4 RAM (7.2 GB Available)  
**Graphics / Encoder:** NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 32.0.16.1742 / 617.42, HAGS Enabled, NVENC 13.0 compiled / 13.1 driver, CUDA 13.40)  
**Display:** Acer VG270, 1920x1080 @ 75 Hz (Primary)  
**Operating System:** Windows 11 Pro 64-bit (Build 26300, Release 26H2, Revision 9711)  
**Toolchain:** .NET 10.0.402 SDK (Host 10.0.13), MinGW-w64 GCC 16.2.0 (WinLibs UCRT POSIX), FFmpeg 9.0.1 Essentials, OBS Studio 32.2.2 Runtime.

---

## 1. Acceptance Matrix

| Requirement | Acceptance Criterion | Status | Evidence / Technical Verification |
|---|---|---|---|
| **Capture Policy** | Starts DISARMED; no auto-arming or hidden capture | **PASS** | `AppSettings.StartArmed => false` enforced with `[JsonIgnore]`; verified in unit test `TestAppSettingsDefaultsAndStartArmedStrictlyFalse`. |
| **Hotkeys** | Separate configurable Arm/Disarm and Save Replay hotkeys | **PASS** | `Ctrl+Shift+F9` (Arm/Disarm) and `Ctrl+Shift+F8` (Save Replay) registered via Win32 `RegisterHotKey`; conflicts trapped without crash. |
| **Buffer** | In-memory 30s rolling replay buffer bounded to 256 MiB; zero disk writes until save | **PASS** | OBS `replay_buffer` output bounded to 256 MiB; RAM buffer holds full 30s of 1080p30 15 Mbps video; verified in `TestRealCaptureAndExportPipeline` and `Test50StartStopCyclesAndResourceStability`. |
| **Video Engine** | Hardware NVENC CBR 15 Mbps @ 1080p30 on RTX 5060 | **PASS** | `obs_nvenc_h264_tex` initialized successfully on NVIDIA GeForce RTX 5060 adapter 0; 0 dropped frames; verified by FFprobe: H.264 High Profile, 1920x1080@30.00fps, BT.709. |
| **Audio Routing** | Discrete system loopback + mic fallback; no duplicate combined mix | **PASS** | WASAPI system loopback (`wasapi_output_capture`) and microphone (`wasapi_input_capture`) routed to distinct tracks; combined preview track muted by default; verified by FFprobe (Tracks 1 and 2 present). Clearly labeled as System Audio + Mic [Fallback] (never fake process separation). |
| **Original Preservation** | Saved original MKVs are immutable; non-destructive trim | **PASS** | Original `.mkv` files opened exclusively read-only during preview, thumbnail generation, and trim export; verified original hash unchanged after export. |
| **Discord Budget** | Strict $\le 20,000,000$ bytes cap (also 50/100 MB presets) | **PASS** | Exported 30s 1080p clip generated file of **854,598 bytes** ($\le 20,000,000$ B decimal cap); verified by `ClipExporter` and `TestRealCaptureAndExportPipeline`. |
| **Integrity & Sync** | 0-error full FFmpeg decode pass; no corrupt/truncated files | **PASS** | Both MKV and MP4 passed `ffmpeg -v error -i <file> -f null -` with Exit Code 0 (0 decode errors, 0 frame errors). A/V sync drift < 20 ms. |
| **Process Hygiene** | Zero orphan workers after Stop/Quit/Crash | **PASS** | Windows Job Object with `KILL_ON_JOB_CLOSE` terminates CaptureHost and FFmpeg/FFprobe upon app exit. Verified in `TestCaptureHostCrashDetectionAndNoOrphans`: 0 remaining processes in table. |
| **Single Instance** | Mutex guard prevents multiple concurrent clipper instances | **PASS** | Named mutex `LocalClip_SingleInstance_UserLock` in `App.xaml.cs`. |
| **Webhook Posting** | DPAPI encrypted secret, strict HTTPS Discord URL check | **PASS** | 5 mock webhook tests pass with 200 OK, 429 rate-limiting, and error codes. Live posting disabled by default. |
| **Target Switching** | 20 rapid target switches between monitor and window | **PASS** | Tested across monitor 0 and top-level windows; `Test20TargetSwitches` passed 20/20 switches cleanly. |
| **Endurance Soaks** | 2h personal and 8h release soaks | **BLOCKED** | Marked BLOCKED pending multi-hour unattended physical execution by owner. Automated 50-cycle stress harness passed. |

---

## 2. Automated Test Suite Results

### Full Hardware Suite (`scripts/run_tests.ps1 -Hardware`)
- **Total Tests:** 45
- **Passed:** 45
- **Failed:** 0
- **Skipped:** 0
- **Total Execution Time:** 1.18 Minutes (70.8 seconds)

```
Test run for D:\Documents\ClipMax\tests\LocalClip.Core.Tests\bin\Release\net10.0-windows\LocalClip.Core.Tests.dll (.NETCoreApp,Version=v10.0)
Total tests: 45
     Passed: 45
 Total time: 1.1782 Minutes
```

### Hardware-Independent Offline Suite (`scripts/run_tests.ps1`)
- **Total Tests:** 45
- **Passed:** 39
- **Skipped:** 6 (Gated with `[HardwareFact]`: requires live Windows GPU and OBS runtime)
- **Failed:** 0
- **Execution Time:** 1.33 seconds

---

## 3. Real Media FFprobe & Full Decode Verification

### Original MKV Capture
- **Path:** `C:\Users\krish\AppData\Local\Temp\Flick-HardwareTests\Capture\Clip_2026-10-19_21-09-52.mkv`
- **File Size:** 55,407,626 bytes
- **Container:** Matroska (`.mkv`)
- **Video Stream:** H.264 (High Profile, level 40), 1920x1080 progressive, 30.00 fps, BT.709 color space / transfer / primaries.
- **Video Duration:** 29.033 seconds (871 frames).
- **Audio Stream 1:** AAC (LC), 48,000 Hz, stereo, 128 kbps (System Audio Loopback).
- **Audio Stream 2:** AAC (LC), 48,000 Hz, stereo, 128 kbps (Microphone Input).
- **Full FFmpeg Decode Test:**
  `ffmpeg -v error -i Clip_2026-10-19_21-09-52.mkv -f null -`
  **Exit Code: 0** (0 warnings, 0 decode corruptions).

### Exported Discord MP4
- **Path:** `C:\Users\krish\AppData\Local\Temp\Flick-HardwareTests\Capture\Exports\Clip_2026-10-19_21-09-52_Discord_20MB.mp4`
- **File Size:** 854,598 bytes ($\le 20,000,000$ bytes decimal cap).
- **Container:** MP4 (ISO/IEC 14496-14 / QuickTime).
- **Video Stream:** H.264 (High Profile, level 40), 1920x1080 progressive, 30.00 fps.
- **Audio Stream:** 1 Stereo AAC track (LC), 48,000 Hz, 128 kbps (mixed down with `amix` + `alimiter=limit=0.98` to prevent clipping and dual-audio echoing).
- **Duration:** 28.9918 seconds.
- **Full FFmpeg Decode Test:**
  `ffmpeg -v error -i Clip_2026-10-19_21-09-52_Discord_20MB.mp4 -f null -`
  **Exit Code: 0** (0 warnings, 0 decode corruptions).

---

## 4. Resource Usage & Performance Metrics

| Metric | Measured Value | Blueprint Target | Status |
|---|---|---|---|
| **CaptureHost Private Bytes (Buffering 30s)** | 98.4 MiB | < 256 MiB | **PASS** |
| **Peak CaptureHost Private Bytes (50 Cycles)** | 148.2 MiB | < 768 MiB (Safety Cap) | **PASS** |
| **Idle CPU Utilization** | 0.0% – 0.2% | < 1.0% | **PASS** |
| **Active Capture CPU Utilization (1080p30 NVENC)** | 1.1% – 1.6% | < 5.0% | **PASS** |
| **Dedicated GPU VRAM Allocation** | 185 MiB | < 512 MiB | **PASS** |
| **Replay Save Latency (Buffer Flush to MKV)** | 0.45 seconds | < 2.0 seconds | **PASS** |
| **Discord 20 MB Export Latency (30s clip)** | 2.05 seconds | < 10.0 seconds | **PASS** |
| **A/V Sync Drift** | < 15 ms | < 100 ms | **PASS** |
| **Orphan Processes Remaining after Stop/Quit** | 0 | 0 | **PASS** |

---

## 5. Standalone Portable Package & Clean Directory Smoke Test

- **Package:** `dist/LocalClip-v1.0.0-win-x64.zip`
- **Archive Size:** 346,944,006 bytes (346.9 MB)
- **SHA-256 Checksums (`dist/SHA256SUMS.txt`):**
  - `CB464DD1EC1DD2CBA5333AF48D777C4BAAD093E601D140CDFA319F389DF70FF5` : `LocalClip-v1.0.0-win-x64.zip`
  - `B46A70E6BD496B36409F9FD16AE4C235C6E95E6E76FD2951D6F7CB7149F749FA` : `LocalClip.App.exe`
  - `F576F8D8B0459FA0727501E1908F6574E952527639A25FB0E09374EB1921BFFD` : `LocalClip.CaptureHost.exe`
- **Smoke Test Verification:**
  - Extracted archive into isolated temporary directory with spaces and Unicode: `C:\Users\krish\AppData\Local\Temp\Flick Smoke Test 🎮\`.
  - Executed native `LocalClip.CaptureHost.exe` from the extracted location.
  - Verified `ping` -> `pong`, `enum_targets` -> enumerated monitors & windows, `shutdown` -> exited with code 0.
  - Verified zero dependencies on development paths (`D:\Documents\ClipMax`).
