# LocalClip / Flick Implementation State

**Repository:** `Krish-Verma/Flick`  
**Active Branch:** `fix/backend-stability-and-ci-20261019` (Draft PR #1)  
**Qualified Host Platform:** Windows 11 Pro 64-bit (Build 26300), AMD Ryzen 5 5600 6-Core, 16 GB DDR4 RAM, NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 617.42)  
**Qualification Date:** October 20, 2026  
**State:** Backend Technical Hardware Qualification Complete (M0, M1, M2, M3, M4 Verified on Hardware)

---

## 1. Executive Summary

Flick's clipping backend has undergone full hardware qualification and stabilization on the local Windows 11 PC equipped with an NVIDIA GeForce RTX 5060 GPU. All **54 automated tests** pass (47 hardware-independent + 7 hardware-dependent tests in 2.22 minutes). The complete clipping-to-Discord pipeline has been proven with real NVENC hardware encoding, in-memory 30s circular buffering, disk saving to Matroska (.mkv), non-destructive trimming, audio mixing, and export to a strictly capped 20 MB MP4 container.

High-motion video capture, visual entropy, and full zero-error FFmpeg decodes have been physically verified. Monitor capture has been hardened for Windows 11 / HAGS using Windows Graphics Capture (WGC / WinRT method 2) with dynamic display identifier resolution, eliminating the DXGI 0x0 black-frame defect. Memory stability across 50 start/stop cycles has been proven with peak private bytes bounded to 156 MiB (< 768 MiB safety cap).

---

## 2. Key Stabilization & Hardware Fixes Applied

1. **Windows Graphics Capture (WGC) for Display Duplication:**
   - In `src/LocalClip.CaptureHost/main.cpp`, replaced DXGI Desktop Duplication (`method = 0`) with Windows Graphics Capture (`method = 2`) and added dynamic `monitor_id` resolution via `EnumDisplayMonitors`. This resolves DXGI `887A0004` errors in non-elevated sessions, capturing real, vibrant desktop and gameplay frames instead of black 0x0 dummy frames.
2. **Video Engine Re-initialization & Memory Bounds:**
   - In `ensure_obs_init`, cached current width, height, and FPS parameters to prevent redundant `obs_reset_video` calls during start/stop cycles. Disconnected the `saved` signal on capture stop. Reduced peak host memory from 931 MiB down to ~156 MiB across 50 consecutive cycles.
3. **IPC Stop/Shutdown Deadline & Timeout Synchronization:**
   - In `internal_stop_capture`, tuned the graceful replay buffer output stop deadline to 2.0 seconds before `obs_output_force_stop`, and synchronized `g_state_mutex` updates.
   - In `src/LocalClip.Core/Ipc/CaptureHostClient.cs`, increased `StopCaptureAsync` timeout to 10,000 ms and `ShutdownAsync` to 8,000 ms, eliminating timeout races during rapid stress cycling.
4. **Assembly-Level Hardware Test Serialisation:**
   - Added `[assembly: CollectionBehavior(DisableTestParallelization = true)]` to prevent xUnit from running GPU capture sessions concurrently, preventing driver-level NVENC resource contention during automated test runs.
5. **OBS Replay Buffer Path Resolution & Unique Immutability:**
   - Integrated the OBS `get_last_replay` proc handler call with write-time directory verification and UUID assignment. Rapid consecutive saves generate unique immutable `.mkv` files without overwriting or relabeling older clips.
6. **Desktop Window Handle Attachment:**
   - In `src/LocalClip.CaptureHost/main.cpp`, integrated `OpenDesktopW(L"default")` and `SetThreadDesktop` at startup and window validation points to enable user32 `IsWindow` checks under runner sessions.
7. **Subprocess Confinement via Windows Job Object:**
   - Worker processes (OBS capture host, FFmpeg, FFprobe) are confined to an owned Windows Job Object configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Zero orphan workers remain after stop, quit, or crash.
8. **Audio Track Muting & Accurate Fallback Labeling:**
   - Discrete audio sources (System Audio + Microphone) are routed to separate tracks. Track 1 (combined mix) is muted by default in preview and export filters to prevent duplicate audio echoing. Labeled as "System Audio + Mic [Fallback]".
9. **Strict 20 MB Decimal Cap Enforcement:**
   - Bitrate budget calculator treats 20 MB as exactly $20,000,000$ bytes (not $20,971,520$ B), reserving a safety margin of at least $\max(5\%, 256\text{ KiB})$ to prevent Discord upload rejection. High-motion 30s export measured at **18,763,446 bytes**.

---

## 3. Pinned Toolchain & Dependency Inventory

| Tool / Dependency | Version | Location / Source | Hash / Verification |
|---|---|---|---|
| **.NET SDK** | 10.0.402 (Host 10.0.13) | `C:\Users\krish\AppData\Local\Microsoft\dotnet` | MIT; Builds WPF & Core projects |
| **MinGW-w64 GCC** | 16.2.0 (UCRT, POSIX) | WinLibs distribution | GPLv3 / LGPL; Builds `LocalClip.CaptureHost.exe` |
| **OBS Runtime** | 32.2.2 (Win x64) | `tools/obs/runtime` | SHA-256 verified; libobs, obs-nvenc, win-wasapi, win-capture |
| **FFmpeg / FFprobe** | 9.0.1 Essentials | `tools/ffmpeg` | SHA-256 verified; Gyan.dev build |

---

## 4. Verification Evidence & Artifacts

- **Full Hardware Suite:** 54 tests passed in 2.22 minutes (`scripts/run_tests.ps1 -Hardware`).
- **Offline Suite:** 47 tests passed, 7 skipped in 1.48 seconds (`scripts/run_tests.ps1`).
- **Original MKV:** 55,750,884 bytes, 29.10s, 1920x1080@30fps NVENC CBR 15 Mbps, 2 audio tracks (System loopback + Mic), 0 decode errors.
- **Motion Quality:** 58/58 distinct frame hashes via `scripts/check_motion.ps1`.
- **Discord MP4:** 18,763,446 bytes ($\le 20,000,000$ B), 28.99s, 1920x1080@30fps H.264, 1 stereo AAC track, 0 decode errors.
- **Stress Cycles:** 50 start/stop cycles passed with host private bytes bounded to 156.4 MiB (safety budget: 768 MiB).
- **Target Switches:** 20 target switches between monitor and desktop windows passed.
- **Crash Isolation:** Killed host verified to leave 0 orphan processes in table.
- **Portable ZIP:** `dist/LocalClip-v1.0.0-win-x64.zip` (346.9 MB, SHA-256 `ADDA9234D56E4E72994A686D5B0F1A8AF245AE2545157EB02ECFFF15099E9EF2`).
- **Smoke Test:** Extracted and executed cleanly in `%TEMP%\Flick Final Qualification 🎮 3112cb4aa2864a86b41cdb16947f57d9\`. Tested native host ping/shutdown, GUI launch `--minimized`, installer/uninstaller, and Windows startup registry opt-in/restore.

---

## 5. Next Steps

1. Commit and push backend qualification changes to branch `fix/backend-stability-and-ci-20261019` for PR #1.
2. Keep PR draft until owner review.
3. Transition from minimal functional WPF UI to the planned polished GUI redesign.
