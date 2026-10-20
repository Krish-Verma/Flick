# Flick — AntiGravity Windows Qualification and Release Handoff

**Scope:** Technical qualification of Flick's Windows clipping backend on branch `fix/backend-stability-and-ci-20261019` (Draft PR #1). Authoritative specification: `ClipperBlueprint/WINDOWS-CLIPPER-BLUEPRINT.md`.

**Branch:** `fix/backend-stability-and-ci-20261019`  
**Target Platform:** Windows 11 Pro 64-bit (Build 26300), AMD Ryzen 5 5600 6-Core, 16 GB DDR4 RAM, NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 617.42).  
**Hardware Qualification Status:** PASSED (All 54 automated tests pass on live GPU and OBS runtime).

---

## 1. Technical Execution Summary

1. **Hardware & Toolchain Inventory:**
   - Captured real machine environment: NVIDIA GeForce RTX 5060 (PCI ID `10de:2d05`, Driver `32.0.16.1742` / `617.42`, HAGS enabled), AMD Ryzen 5 5600 6-Core, Windows 11 Build 26300.
   - Pinned dependencies verified: .NET 10.0.402 SDK, MinGW-w64 GCC 16.2.0 (WinLibs UCRT), FFmpeg 9.0.1 Essentials, OBS Studio 32.2.2 runtime.
2. **Native CaptureHost & Replay Buffer Fixes:**
   - Recompiled `LocalClip.CaptureHost.exe` cleanly with MinGW GCC.
   - Fixed monitor capture black frames: replaced DXGI Desktop Duplication with Windows Graphics Capture (`method = 2`) and added dynamic `monitor_id` resolution.
   - Fixed memory growth during 50 start/stop cycles: cached video settings to prevent repeated `obs_reset_video` calls and disconnected `saved` signals on stop. Peak private bytes bounded to ~156 MiB.
   - Synchronized IPC deadlines: tuned `internal_stop_capture` graceful deadline to 2.0s before force-stopping, and increased client timeouts to 10s for stop and 8s for shutdown.
   - Fixed replay save signal handling: OBS Studio's `saved` signal on Windows does not set the `path` argument in calldata; integrated `proc_handler_call("get_last_replay")` on `g_cap.replay_output` with verified directory write-time fallback.
   - Fixed window capture verification: added `OpenDesktopW(L"default")` and `SetThreadDesktop` to enable user32 `IsWindow` checks when running under runner sessions.
3. **Automated Testing Reality:**
   - Offline suite (`scripts/run_tests.ps1`): **47 passed, 7 skipped** (hardware tests gated with `[HardwareFact]`).
   - Hardware suite (`scripts/run_tests.ps1 -Hardware`): **54 passed, 0 failed, 0 skipped** (2.22 min run).
   - Validated tests:
     - `TestCaptureHostLifecycleAndIpc`: launch, ping, enum targets, shutdown.
     - `TestRealCaptureAndExportPipeline`: 30s rolling replay buffer on RTX 5060 NVENC, Matroska MKV save, non-destructive trim, Discord 20 MB export.
     - `TestBackToBackReplaySavesAreDistinctAndImmutable`: rapid back-to-back saves produce distinct non-overwriting files with unique UUIDs.
     - `Test50StartStopCyclesAndResourceStability`: 50 consecutive capture start/stop cycles with private bytes bounded to 156.4 MiB (< 768 MiB budget).
     - `Test20TargetSwitches`: 20 consecutive monitor/window target switches.
     - `TestCaptureHostCrashDetectionAndNoOrphans`: killed host process, verified client fault handling and 0 orphan processes in table.
     - `TestSaveDuringStopRejection`: rejection of saves while disarmed.
     - `RecordingProfileTests`: verified Light 720p30, Balanced 1080p30, and Smooth 1080p60 presets, memory budget calculation, and startup opt-in.
4. **Media Validation & Full Decode:**
   - Probed MKV: H.264 High Profile 1920x1080@30fps NVENC CBR 15 Mbps, BT.709, 2 discrete audio tracks (System loopback + Mic), 29.100s. Full FFmpeg decode exit code 0 (zero errors).
   - Motion verified: `scripts/check_motion.ps1` confirms 58/58 distinct frame hashes, 0 black frames, real RGB pixels `[123, 59, 53]`.
   - Probed MP4: H.264 1920x1080@30fps, 1 stereo AAC track (mixed with limiter to prevent double-mix audio), 18,763,446 bytes ($\le 20,000,000$ B cap), 28.992s. Full FFmpeg decode exit code 0 (zero errors).
5. **Packaging & Clean Directory Smoke Test:**
   - Generated self-contained portable package `dist/LocalClip-v1.0.0-win-x64.zip` (346.9 MB) and checksums `dist/SHA256SUMS.txt`.
   - Extracted archive into `%TEMP%\Flick Final Qualification 🎮 <GUID>\` and executed native `LocalClip.CaptureHost.exe`. Verified ping and shutdown without any workspace dependency.
   - Launched `LocalClip.App.exe --minimized` from the extraction folder; verified DISARMED startup, system tray lifecycle, and clean exit.
   - Verified `install.ps1` and `uninstall.ps1` and tested per-user Windows startup registry key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Flick`).

---

## 2. Status of Acceptance Gates

| Gate | Status | Notes |
|---|---|---|
| **M0: Environment & Discovery** | **PASS** | Toolchains, licenses, NVENC discovery verified. |
| **M1: Core Capture Engine** | **PASS** | 30s buffer, NVENC CBR 15 Mbps, WGC display capture, MKV save, decode 0 errors. |
| **M2: Minimal App & Export** | **PASS** | Disarmed start, separate hotkeys, trim/mix, 20 MB cap (18.76 MB generated). |
| **M3: Discord Webhooks** | **PASS (Mock)** | DPAPI protected, strict regex, rate-limit retry tested. |
| **M4: Hardening & Packaging** | **PASS** | Job Object confinement, portable ZIP, SHA-256 verified, Unicode smoke test passed. |
| **Multi-Hour Soak (2h/8h)** | **BLOCKED** | Requires multi-hour physical user session; 50-cycle automated stress passed. |

---

## 3. Recommended Owner Action

1. Review PR #1 updates on branch `fix/backend-stability-and-ci-20261019`.
2. Perform native drag/paste test of the exported MP4 directly into the real Discord desktop composer.
3. Keep PR draft until owner approval; proceed to the planned minimal-to-polished WPF GUI transformation.
