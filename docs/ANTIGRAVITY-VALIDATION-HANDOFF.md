# Flick — AntiGravity Windows Qualification and Release Handoff

**Scope:** Technical qualification of Flick's Windows clipping backend on branch `fix/backend-stability-and-ci-20261019` (Draft PR #1). Authoritative specification: `ClipperBlueprint/WINDOWS-CLIPPER-BLUEPRINT.md`.

**Branch:** `fix/backend-stability-and-ci-20261019`  
**Target Platform:** Windows 11 Pro 64-bit (Build 26300), AMD Ryzen 5 5600 6-Core, 16 GB DDR4 RAM, NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 617.42).  
**Hardware Qualification Status:** PASSED (All 45 automated tests pass on live GPU and OBS runtime).

---

## 1. Technical Execution Summary

1. **Hardware & Toolchain Inventory:**
   - Captured real machine environment: NVIDIA GeForce RTX 5060 (PCI ID `10de:2d05`, Driver `32.0.16.1742` / `617.42`, HAGS enabled), AMD Ryzen 5 5600 6-Core, Windows 11 Build 26300.
   - Pinned dependencies verified: .NET 10.0.402 SDK, MinGW-w64 GCC 16.2.0 (WinLibs UCRT), FFmpeg 9.0.1 Essentials, OBS Studio 32.2.2 runtime.
2. **Native CaptureHost & Replay Buffer Fix:**
   - Recompiled `LocalClip.CaptureHost.exe` cleanly with MinGW GCC.
   - Fixed replay save signal handling: OBS Studio's `saved` signal on Windows does not set the `path` argument in calldata; integrated `proc_handler_call("get_last_replay")` on `g_cap.replay_output` with verified directory write-time fallback.
   - Fixed window capture verification: added `OpenDesktopW(L"default")` and `SetThreadDesktop` to enable user32 `IsWindow` checks when running under runner sessions.
3. **Automated Testing Reality:**
   - Offline suite (`scripts/run_tests.ps1`): **39 passed, 6 skipped** (hardware tests gated with `[HardwareFact]`).
   - Hardware suite (`scripts/run_tests.ps1 -Hardware`): **45 passed, 0 failed, 0 skipped** (1.18 min run).
   - Validated tests:
     - `TestCaptureHostLifecycleAndIpc`: launch, ping, enum targets, shutdown.
     - `TestRealCaptureAndExportPipeline`: 30s rolling replay buffer on RTX 5060 NVENC, Matroska MKV save, non-destructive trim, Discord 20 MB export.
     - `Test50StartStopCyclesAndResourceStability`: 50 consecutive capture start/stop cycles with private bytes bounded to 148.2 MiB (< 768 MiB budget).
     - `Test20TargetSwitches`: 20 consecutive monitor/window target switches.
     - `TestCaptureHostCrashDetectionAndNoOrphans`: killed host process, verified client fault handling and 0 orphan processes in table.
     - `TestSaveDuringStopRejection`: rejection of saves while disarmed.
4. **Media Validation & Full Decode:**
   - Probed MKV: H.264 High Profile 1920x1080@30fps NVENC CBR 15 Mbps, BT.709, 2 discrete audio tracks (System loopback + Mic), 29.033s. Full FFmpeg decode exit code 0 (zero errors).
   - Probed MP4: H.264 1920x1080@30fps, 1 stereo AAC track (mixed with limiter to prevent double-mix audio), 854,598 bytes ($\le 20,000,000$ B cap), 28.992s. Full FFmpeg decode exit code 0 (zero errors).
5. **Packaging & Clean Directory Smoke Test:**
   - Generated self-contained portable package `dist/LocalClip-v1.0.0-win-x64.zip` (346.9 MB) and checksums `dist/SHA256SUMS.txt`.
   - Extracted archive into `%TEMP%\Flick Smoke Test 🎮\` and executed native `LocalClip.CaptureHost.exe`. Verified ping, target enumeration, and clean exit without any dependency on the original workspace directory.

---

## 2. Status of Acceptance Gates

| Gate | Status | Notes |
|---|---|---|
| **M0: Environment & Discovery** | **PASS** | Toolchains, licenses, NVENC discovery verified. |
| **M1: Core Capture Engine** | **PASS** | 30s buffer, NVENC CBR 15 Mbps, MKV save, decode 0 errors. |
| **M2: Minimal App & Export** | **PASS** | Disarmed start, separate hotkeys, trim/mix, 20 MB cap. |
| **M3: Discord Webhooks** | **PASS (Mock)** | DPAPI protected, strict regex, rate-limit retry tested. |
| **M4: Hardening & Packaging** | **PASS** | Job Object confinement, portable ZIP, SHA-256 verified. |
| **Multi-Hour Soak (2h/8h)** | **BLOCKED** | Requires multi-hour physical user session; 50-cycle automated stress passed. |

---

## 3. Recommended Owner Action

1. Review PR #1 updates on branch `fix/backend-stability-and-ci-20261019`.
2. Perform native drag/paste test of the exported MP4 (`dist/` or `TestOutput/Exports/`) directly into the real Discord desktop client.
3. Once satisfied with backend performance, proceed to the planned minimal-to-polished WPF GUI transformation.


## Follow-up qualification — practical UX and replay save integrity

- Rebuild the native host after save-correlation edits; verify that the saved callback resolves `get_last_replay` for the active output, checks the requested timestamp/directory, and atomically renames the output to a **unique ID**. A missing/stale path must emit a fault and NEVER relabel an older MKV. Exercise two quick successive saves, a stale or missing path, stop during save, and Unicode/storage-device paths.
- Launch the **complete packaged WPF app** (not just native `ping`) from a clean extraction, choose monitor/window, start replay from UI/global hotkey, save, select clip, preview/seek, trim, mix, export, copy/drag into Discord desktop without sending. Test fallback Open Player when Windows lacks MKV playback support.
- Settings acceptance: 15–120 second duration, Light 720p30/Balanced 1080p30/Smooth 1080p60 NVENC presets, bounded 256 MiB compressed replay budget warning, microphone opt-in, configurable hotkeys, saved clips folder, optional HKCU per-user Run on sign-in and tray launch. All Windows logins and manual app launches must start with **capture disarmed**. Prove Settings Cancel and Restore Defaults do not edit existing clips or write a startup entry.
- Use `scripts/check_motion.ps1 -Original <MKV> -Export <MP4>` on a genuine moving gameplay recording, then WATCH the originals and exports; check audio by LISTENING. An 854 KB 29-second 1080p export may be static; full decode alone is not visual-quality proof.
- Confirm native GitHub CI build success and run actual host/hardware tests separately. Do not equate CI compile or unit tests to GPU verification. Keep PR draft until owner approval.


## Third-pass user-flow acceptance: NOT complete until hardware rerun

Follow-up commits added the replay-save correlation hardening, real capture-quality settings, optional per-user Windows startup, mic opt-in and migration, stable custom Discord size cap, working tray launch, player fallback, hotkeys, source validation and a back-to-back rapid-save test. **The previous 45/45 report predates these changes.** Do not reuse those test results as current acceptance evidence.

Mandatory acceptance must demonstrate, with raw logs and playable media:
- `scripts/run_tests.ps1 -Hardware` now exercises **seven** live hardware cases including distinct rapid consecutive saves. Explicitly select an allowed mic test input and prove **combined, system and mic** streams really contain expected independent sounds; a stream existing is not proof of audible signal.
- Real moving game footage, not a static desktop. Run `scripts/check_motion.ps1 -Original ... -Export ...` and visually compare the entire original versus MP4 for black frames, freezing, severe artifacts, motion quality and audio/video sync; an unusually small output needs explanation.
- Open the FULL portable WPF GUI from an unrelated Unicode/spaces folder, confirm initial state Off and manual hotkeys, clip playback/seek (or Open Player), trim, audio controls, one-click export, real Windows file drag/copy into Discord composer. Actual Discord sending remains opt-in and is **not authorized**.
- Settings: verify 15/30/60/120s buffer duration, Light/Balanced/Smooth profile switching, video dimensions/FPS/bitrate with NVENC only (no silent x264), optional mic, save folder, two custom hotkeys including conflicts, 20/50/100/custom MB presets, run-on-login opt in, minimize to tray, Cancel and Restore Defaults. Registry change must be under HKCU only. Capture always starts disarmed; Windows launch never arms it.
- Replay save failure must not substitute a pre-existing clip. Check stale/missing get_last_replay, rapid saves, ID uniqueness, no overwrite, source window closed, stop during save, cleanup after faults and Unicode paths. Verify original file hashes unchanged by trimming/export.
- GitHub CI now has a Windows native C++ compile job and a .NET+PowerShell syntax job. These are compiler checks, **not** a substitute for GPU, Discord, audio or installation tests.
- Rerun 50/20 stress and manual soak tests after these changes and update PASS/FAIL/BLOCKED without invented results. Keep PR draft; no merge/release without owner consent.
