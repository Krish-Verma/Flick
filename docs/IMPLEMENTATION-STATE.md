# LocalClip / Flick Implementation State

**Repository:** `Krish-Verma/Flick`  
**Active Branch:** `fix/backend-stability-and-ci-20261019` (Draft PR #1)  
**Qualified Host Platform:** Windows 11 Pro 64-bit (Build 26300), AMD Ryzen 5 5600 6-Core, 16 GB DDR4 RAM, NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 617.42)  
**Qualification Date:** October 20, 2026  
**State:** Backend Technical Qualification Complete (M0, M1, M2, M3, M4 Verified on Hardware)

---

## 1. Executive Summary

Flick's clipping backend has been hardened, compiled, and hardware-qualified on the local Windows 11 PC equipped with an NVIDIA GeForce RTX 5060 GPU. All 45 automated tests pass (39 offline + 6 hardware-dependent tests). The capture-to-Discord pipeline has been proven with real NVENC hardware encoding, in-memory 30s circular buffering, disk saving to Matroska (.mkv), non-destructive trimming, audio mixing, and export to a strictly capped 20 MB MP4 container.

---

## 2. Key Stabilization & Hardware Fixes Applied

1. **OBS Replay Buffer Path Resolution:**
   - In `src/LocalClip.CaptureHost/main.cpp`, integrated the OBS `get_last_replay` proc handler call on `g_cap.replay_output` with a fallback to newest `.mkv` write-time verification in the configured output directory. This resolved the OBS `saved` signal omitting the `path` argument in calldata on Windows.
2. **Desktop Window Handle Attachment:**
   - In `src/LocalClip.CaptureHost/main.cpp`, integrated `OpenDesktopW(L"default")` and `SetThreadDesktop` at startup and window validation points. This ensures user32 `IsWindow` checks succeed even when the host or test suite is executed in non-interactive or background runner sessions.
3. **Dynamic Plugin & Runtime Resolution:**
   - Removed all hardcoded `D:\Documents\ClipMax` development paths across `CaptureHost` and `LocalClip.Core`. Binaries determine relative runtime locations dynamically.
4. **Subprocess Confinement via Windows Job Object:**
   - All worker processes (OBS capture host, FFmpeg, FFprobe) are assigned to an owned Windows Job Object configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. This guarantees zero orphan processes after stop, exit, or crash.
5. **Combined Preview Track Muting:**
   - When discrete audio sources (System Audio + Microphone) exist, track 1 (combined mix) is muted by default in preview and export filters to prevent double-mix audio echoing. Audio mode is accurately labeled as "System Audio + Mic [Fallback]".
6. **Strict 20 MB Decimal Cap Enforcement:**
   - Bitrate budget calculator treats 20 MB as exactly $20,000,000$ bytes (not $20,971,520$ B), reserving a safety margin of at least $\max(5\%, 256\text{ KiB})$ to prevent Discord upload rejection.
7. **CI Hardware Test Gating:**
   - Gated the 6 GPU-dependent tests with `[HardwareFact]` keyed to `$env:FLICK_HARDWARE_TESTS == "1"`. GitHub-hosted runners run the 39 hardware-independent tests (reporting 6 skipped), while the local NVIDIA PC executes all 45 tests.

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

- **Full Hardware Suite:** 45 tests passed in 1.18 minutes (`scripts/run_tests.ps1 -Hardware`).
- **Offline Suite:** 39 tests passed, 6 skipped in 1.33 seconds (`scripts/run_tests.ps1`).
- **Original MKV:** 55,407,626 bytes, 29.03s, 1920x1080@30fps NVENC CBR 15 Mbps, 2 audio tracks (System loopback + Mic), 0 decode errors.
- **Discord MP4:** 854,598 bytes ($\le 20,000,000$ B), 28.99s, 1920x1080@30fps H.264, 1 stereo AAC track, 0 decode errors.
- **Stress Cycles:** 50 start/stop cycles passed with host private bytes bounded to 148.2 MiB (budget: 768 MiB).
- **Target Switches:** 20 target switches between monitor and desktop windows passed.
- **Crash Isolation:** Killed host verified to leave 0 orphan processes in table.
- **Portable ZIP:** `dist/LocalClip-v1.0.0-win-x64.zip` (346.9 MB, SHA-256 `CB464DD1EC1DD2CBA5333AF48D777C4BAAD093E601D140CDFA319F389DF70FF5`).
- **Smoke Test:** Extracted and executed cleanly in `%TEMP%\Flick Smoke Test 🎮\`.

---

## 5. Next Steps

1. Commit and push backend qualification changes to branch `fix/backend-stability-and-ci-20261019` for PR #1.
2. Conduct user hands-on validation: drag exported clip into Discord desktop client to observe Discord player behavior.
3. Transition from minimal functional WPF UI to the planned polished GUI redesign.
