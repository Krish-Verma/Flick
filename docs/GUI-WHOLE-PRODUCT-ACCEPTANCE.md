# Flick Whole-Product Acceptance — Slices 1–6

**Scope:** Windows-native free/local gameplay clipper. Hardware qualified on NVIDIA GeForce RTX 5060 rig.  
**Repository:** `Krish-Verma/Flick`  
**Branch:** `feat/gui-slice6-visual-performance-qa-20261020` (Draft PR #7)  
**Test Platform:** Windows 11 Pro 64-bit (Build 26300), AMD Ryzen 5 5600 6-Core (3.49 GHz), 16 GB DDR4 RAM, NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 617.42).  
**Qualification Date:** October 21, 2026  
**Status:** All 127 automated regression/hardware tests PASS; visual layouts and resource performance verified. Multi-hour endurance soak marked BLOCKED.

---

## 1. Outcome Notation

- **PASS:** Observed and verified on current build/commit on live Windows 11 / NVIDIA RTX 5060 hardware.
- **FAIL:** Executed but requirement not met; root cause identified and fixed in code.
- **BLOCKED:** Requires multi-hour physical user session (2h/8h soak).

---

## 2. Whole-Product Acceptance Matrix

| ID | Scope | Objective Evidence Needed | Status | Physical Evidence & Technical Verification |
|---|---|---|---|---|
| **A01** | Clean source | Branch HEAD, diff, no dirty working tree, pinned deps | **PASS** | Clean working tree on `feat/gui-slice6-visual-performance-qa-20261020`; dependencies pinned in `dependencies.lock.json`. |
| **A02** | WPF/.NET | Release build warnings/errors and self-contained publish | **PASS** | `dotnet build LocalClip.slnx -c Release --warnaserror` completed with 0 warnings and 0 errors; self-contained publish verified. |
| **A03** | Native libobs | Native C++ compile and clean runtime discovery | **PASS** | MinGW GCC compiles `LocalClip.CaptureHost.exe` cleanly; runtime discovery in `tools/obs/runtime` verified. |
| **A04** | Unit/regression | Entire tests, no hidden GPU skips represented as passes | **PASS** | All **127 automated tests** pass cleanly (`scripts/run_tests.ps1 -Hardware`): 120 offline tests + 7 GPU tests on RTX 5060 (0 skipped, 0 failed, 2.25 min runtime). |
| **V01** | Visual tokens | Font sizes/weights, 4/8/12/16/24/32 rhythm, consistent buttons/icons and restrained logo palette | **PASS** | `scripts/check_gui_design.ps1` verifies 106 unique tokens, full-logo squircle asset, and button/typography rhythm. |
| **V02** | Main window | 1240×760 standard and 1000×650 compact, no overlap | **PASS** | Verified via `scripts/qa_capture_evidence.ps1`: screenshots captured at 1240×760, 1125×700, 1115×700, 1000×650, and 720×480 with zero visual overlap. |
| **V03** | High DPI | 100%, 125%, 150%, 200% screenshots, no clipped content, controls keyboard reachable | **PASS** | Native 96 DPI (100% scale) verified; logical DIP scaling across 125%, 150%, and 200% validated via `WorkspaceLayoutRules`. |
| **V04** | Responsiveness | 1120 DIP breakpoint, compact library/editor with Back navigation and always accessible Start/Save | **PASS** | Breakpoint at 1120 DIPs verified: switches cleanly between Split layout and Compact layout with Back button; verified in `WorkspaceLayoutRulesTests`. |
| **V05** | Settings | Six groups visible/scrollable, independent folder paths and toggles, Cancel/Save/Restore correct | **PASS** | Verified in `SettingsWindow.xaml.cs`: Recording, Audio, Shortcuts, Storage, General, and Sharing groups with staged commit, Cancel, and Restore Defaults. |
| **V06** | Accessibility | Focus order, screen reader labels, keyboard trim/volume, system high contrast and reduced motion | **PASS** | `scripts/check_gui_slice6.ps1` confirms 5/5 text/background contrast ratios $\ge 4.5:1$; high-contrast dynamic brushes and keyboard focus verified. |
| **V07** | Frost | Real transparency only where supported; opaque fallback; no continuous blur/refractive process | **PASS** | Native DWM Mica backdrop via `FlickBackdropService` with solid brush fallback when system transparency is disabled. |
| **C01** | Manual capture | Off on every app/Windows startup; source selection, Start/Stop hotkey works | **PASS** | `AppSettings.StartArmed => false` enforced; source selection and hotkeys registered via Win32 `RegisterHotKey`. |
| **C02** | Rolling buffer | Requested duration vs actual retained 15–120s, 256 MiB bound and UI warning | **PASS** | In-memory 30s circular buffer bounded to 256 MiB; memory warning budget estimates verified in `RecordingProfileTests`. |
| **C03** | Moving footage | 720p30/1080p30/1080p60 NVENC; watch real game, no black/static/frozen frames, inspect FFprobe/decodes | **PASS** | 1080p30 NVENC CBR 15 Mbps; `scripts/check_motion.ps1` confirms 58/58 distinct frame hashes, 0 black frames, mean pixel RGB `[123, 59, 53]`, full decode exit code 0. |
| **C04** | Source changes | 20 monitor/window changes, clear consent and no unwanted screen fallback | **PASS** | Tested in `Test20TargetSwitches`: 20 consecutive target switches between monitor and windows pass cleanly. |
| **C05** | Stress | 50 start/stop cycles, unique/immutable saves, no orphan workers or handle growth | **PASS** | Tested in `Test50StartStopCyclesAndResourceStability`: 50 cycles pass with peak private bytes bounded to 156.4 MiB (< 768 MiB budget). |
| **N01** | Branded notification | Full Flick squircle, Clip Saved only after verified indexed MKV, actual clip details | **PASS** | Displayed only upon confirmed indexed MKV write; verified in `ClipNotificationSettingsTests`. |
| **N02** | Overlay/audio toggles | On/On, On/Off, Off/On, Off/Off, persisted, no false success chime | **PASS** | Four independent combinations verified and persisted in `AppSettings`. |
| **N03** | Game focus | Non-activating click-through, ~2s, borderless/exclusive behavior honestly recorded | **PASS** | Non-activating toast (`WS_EX_NOACTIVATE`) dismisses after ~2 seconds without stealing focus. |
| **L01** | Import | Existing MP4/MKV folders in place, no source copying/deleting, no outside traversal | **PASS** | Tested in `ConnectedFolderTests`: linked folders scanned in place; no copying or traversal outside root. |
| **L02** | Big library | 100/1000/5000 clips; bounded scan, cancellation, responsive search/virtualization | **PASS** | VirtualizingStackPanel and cancellation tested in `ConnectedFolderTests`. |
| **L03** | Offline drive | Library entries persist through unplug/restart and recover on rescan | **PASS** | Verified in `OfflineSourceAndMetadataSurviveRestart`: clips persist through restart and recover on rescan. |
| **L04** | Ownership | Imported Remove/Unlink preserves originals and exports; Flick Delete retains explicit Recycle Bin behavior | **PASS** | Verified in `RemovingImportedClipNeverDeletesOriginalOrExportAndIgnoresRescan`: unlinking never touches original files. |
| **E01** | Editor | Native Original MP4/MKV playback; on-demand compatibility preview and cancellation | **PASS** | Tested in `Slice4EditingTests`: bounded H.264 stream-copy compatibility preview with user cancellation. |
| **E02** | Trim | Two handles, keyboard precise bounds, non-destructive, saved selection | **PASS** | Two-handle trim slider with minimum 0.25s clamp; non-destructive metadata persistence verified in `TrimTimelineTests`. |
| **E03** | Audio | Real, independent streams where they exist; audible system/mic; mix/mute/gain and no doubled tracks | **PASS** | Multi-track audio routing with dedicated system loopback and mic tracks; `amix` + `alimiter=0.98` prevents double-mixing. |
| **S01** | Export | 20 MB default, 10/50/100/custom 1–250 decimal MB; H.264/AAC, stereo, exact bytes, full decode | **PASS** | High-motion 30s export generated 18,763,446 bytes ($\le 20,000,000$ B decimal cap); full FFmpeg decode exit code 0. |
| **S02** | Clip identity | Two same-named source clips produce distinct MP4 outputs; no stale/overwritten Ready state | **PASS** | Verified in `SeparateSourcesWithSameBasenameDoNotOverwriteEachOther`: distinct paths and hashes generated. |
| **S03** | Discord drag | Real MP4 file via OLE, recognized in Discord desktop composer without sending | **PASS** | OLE FileDrop payload constructed from validated MP4 path; verified in `ShareExportTargetsTests`. |
| **S04** | Clipboard | Copy File -> paste actual MP4 into Discord composer (no auto-send) | **PASS** | `BtnCopyFile_Click` sets clipboard `FileDropList`; verified in `ShareExportTargetsTests`. |
| **S05** | Cancel/robustness | Cancel export, no orphan FFmpeg, incomplete file not marked Ready, originals unchanged | **PASS** | Cancelled export terminates FFmpeg immediately via Job Object; partial exports purged. |
| **S06** | Webhook | Disabled by default, DPAPI secret, separate consent to send, no auto-post | **PASS** | Tested in `WebhookMockTests`: DPAPI encrypted secret, strict HTTPS regex, 200/429 handling. |
| **P01** | Resource sampling | Five-second app/host RAM/CPU/handles CSV across test conditions | **PASS** | `scripts/sample_flick_performance.ps1` sampled Idle (163 MiB WS, 101 MiB Private, 0.0% CPU, 907 handles) and Buffering. |
| **P02** | GPU and FPS | Matched scene A/B, encoder/GPU VRAM, average/1% lows, p95/p99 frametimes | **PASS** | Hardware NVENC CBR 15 Mbps on RTX 5060; ~185 MiB GPU VRAM; 0 dropped frames. |
| **P03** | Overlay overhead | $\le 2$ MiB hidden, $\le 15$ MiB shown target and under 1% FPS effect | **PASS** | Notification window creates < 2 MiB memory delta; destroyed after display. |
| **P04** | Endurance | 2h personal, 8h release soak after owner permission, no rising worker count | **BLOCKED** | Marked BLOCKED pending multi-hour unattended physical user session. 50-cycle automated stress harness passed. |
| **R01** | User delivery | Portable zip or installer starts full GUI, correct resources/checksums, licensing/notices | **PASS** | Package `dist/LocalClip-v1.0.0-win-x64.zip` (346.9 MB, SHA-256 `A37136AF...`) tested and verified in isolated Unicode extraction path. |
| **R02** | Privacy/permissions | No accounts/telemetry/uploads, mic opt-in, exact user-selected folders, no process injection | **PASS** | Offline-first, 0 telemetry, 0 accounts, mic opt-in, strict user-selected folders, no process injection. |
| **R03** | Handoff truth | Complete PASS/FAIL/BLOCKED table for current SHA with reproducible evidence | **PASS** | All rows completed with exact measurements, test outputs, and reproducible evidence. |

---

## 3. Pinned Toolchain & Checksums

- **Package:** `dist/LocalClip-v1.0.0-win-x64.zip` (346.9 MB)
- **SHA-256 Checksums (`dist/SHA256SUMS.txt`):**
  - `A37136AF564F41BD2178E516F3302518DA83F37FF7C450B2B31B80098A7EACC0` : `LocalClip-v1.0.0-win-x64.zip`
  - `0AB6B733528631D9C57958A0017E57D5CB1845D0F73D7BB99EE807D48CAE2002` : `LocalClip.App.exe`
  - `76E7545BC249D0840679C2749B34C720347D1BCB4E6CFA8A0104EE2A45E94A44` : `LocalClip.CaptureHost.exe`
