# LocalClip Implementation State

**Repository Working Name:** LocalClip  
**Branch:** main  
**Initial Platform:** Windows 11 Pro 64-bit (Build 26300), AMD Ryzen 5 5600 6-Core, 16 GB RAM, NVIDIA GeForce RTX 5060 (8 GB VRAM, Driver 617.42)  
**Last Updated:** October 14, 2026 (M0 Complete / M1 In Progress)

---

## 1. Resolved Scope Summary

LocalClip is a native Windows x64 clipping application designed to preserve the SteelSeries Moments workflow (manual arming, keyboard shortcut save of the preceding 30 seconds of gameplay, review/trim, audio source adjustments, and validated watermark-free export below Discord's 20 MB / customizable caps with native drag/copy into Discord). It features strict resource bounds, offline core functionality, zero telemetry, no accounts or paywalls, and clean worker cleanup.

---

## 2. Toolchain and Pinned Dependencies Inventory

All dependencies have been pinned and verified against the actual Windows 11 test machine:

| Dependency | Pinned Version | License | Verification Status | Notes |
|---|---|---|---|---|
| **.NET SDK** | 10.0.402 (Host 10.0.13, MSBuild 18.9.15) | MIT | Verified | Installed to `%LOCALAPPDATA%\Microsoft\dotnet`; supports WPF on .NET 10 |
| **C++ Toolchain** | MinGW-w64 GCC/G++ 16.2.0 (UCRT, POSIX threads) | GPL/LGPL/Public Domain | Verified | WinLibs distribution; includes `cmake 4.4.2`, `ninja`, `ntldd` |
| **OBS Runtime / libobs** | 32.2.2 (Win x64) | GPL-2.0-or-later | Verified | Headers extracted to `tools/obs/include/libobs`; runtime in `tools/obs/runtime` |
| **FFmpeg / FFprobe** | 9.0.1 Essentials (Gyan.dev) | GPL-2.0-or-later | Verified | Copied to `tools/ffmpeg`; verified `h264_nvenc` encode on RTX 5060 |
| **Discord Desktop** | App 1.0.9261 | Proprietary (Target) | Verified | Installed at `%LOCALAPPDATA%\Discord\app-1.0.9261\Discord.exe` |

---

## 3. Capability Discovery Verification (M0 Gate: PASSED)

The diagnostic probe (`test_probe2.exe`) linked against `obs.dll` and executed successfully on the RTX 5060, confirming the following engine modules and IDs:

- **Video Adapters:** Adapter 0: NVIDIA GeForce RTX 5060 (7.7 GiB Dedicated VRAM, D3D11 Feature Level b000)
- **Hardware Video Encoders:**
  - `obs_nvenc_h264_tex` / `jim_nvenc` (NVIDIA NVENC H.264)
  - `obs_nvenc_hevc_tex` / `jim_hevc_nvenc` (NVIDIA NVENC HEVC)
  - `obs_nvenc_av1_tex` / `jim_av1_nvenc` (NVIDIA NVENC AV1)
  - `obs_x264` (Software fallback)
- **Audio Encoders:**
  - `ffmpeg_aac` (FFmpeg AAC)
- **Capture Sources:**
  - `window_capture` (Window Capture - Windows Graphics Capture non-injecting)
  - `monitor_capture` (Display Capture)
  - `game_capture` (Game Capture)
  - `wasapi_input_capture` (Microphone)
  - `wasapi_output_capture` (System Audio Loopback fallback)
  - `wasapi_process_output_capture` (Application Audio Capture for per-process game/Discord isolation)
- **Output Mechanisms:**
  - `replay_buffer` (OBS bounded in-memory replay buffer)
  - `ffmpeg_muxer` (MKV / MP4 muxing)

All modules initialized and shut down cleanly with 0 crashes, 0 memory corruption, and 0 orphan threads.

---

## 4. Current Milestone Status

- [x] **M0: Toolchain, Runtime, License Inventory, and Capture Probe** (PASSED)
- [ ] **M1: Working Capture Host, Real Audio, Replay Save, and Worker Cleanup** (IN PROGRESS)
- [ ] **M2: Local WPF App, Gallery, Playback, Trim/Mix, Discord Exporter, Native Drag/Copy**
- [ ] **M3: Optional Protected Discord Webhook Integration and Mock Tests**
- [ ] **M4: Process Hardening, Job Objects, Recovery, Resource Guards, Packaging**

---

## 5. Next Steps

Proceed directly to implementing M1:
1. Implement `LocalClip.CaptureHost` in native C++:
   - stdio IPC protocol (JSON requests/responses/events)
   - Real window/monitor capture configuration
   - Hardware NVENC encoding (`jim_nvenc`) at 1080p30 / 15 Mbps
   - 3 multi-track audio sources (Game/Process, Mic, Discord/System) via `wasapi_process_output_capture` and `wasapi_input_capture`
   - Bounded 30s replay buffer in memory (256 MiB budget)
   - Save Replay triggering to immutable `.mkv` with unique timestamps
   - Controlled shutdown releasing all D3D11/encoder/audio resources
2. Implement M1 harness / tests:
   - Prove real window capture of an active window
   - Prove hardware NVENC encoding
   - Prove 30s replay buffer saving to playable MKV
   - Verify MKV playability and stream integrity via FFprobe
   - Verify clean process termination and zero worker lingering
