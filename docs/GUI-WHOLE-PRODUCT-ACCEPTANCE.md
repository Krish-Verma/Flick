# Flick Whole-Product Acceptance — Slices 1–6

**Scope:** Windows-native free/local gameplay clipper. This is a requirement-level gate, not a list of claims that testing already passed. PR #7 is a **draft** and **all Windows-only rows are BLOCKED until verified with dated, reproducible evidence.**

## Outcome notation

- **PASS:** observed and verified on the current exact build/commit; link to logs/screenshots/file hashes/measurements.
- **FAIL:** executed but requirement not met; record root cause, fix commit, then rerun until PASS.
- **BLOCKED:** could not execute or lacks a qualified environment, source file, permissions or owner consent; state precisely why. Do **not** count skipped tests as passes.

## Whole-product acceptance matrix

| ID | Scope | Objective evidence needed | Baseline status |
|---|---|---|---|
| A01 | Clean source | Branch HEAD, diff, no dirty working tree, pinned deps | CI-assisted only |
| A02 | WPF/.NET | Release build warnings/errors and self-contained publish | CI-assisted only |
| A03 | Native libobs | Native C++ compile and clean runtime discovery | Compile in CI; runtime BLOCKED |
| A04 | Unit/regression | Entire tests, no hidden GPU skips represented as passes | CI-assisted only |
| V01 | Visual tokens | Font sizes/weights, 4/8/12/16/24/32 rhythm, consistent buttons/icons and restrained logo palette | Static CI; render BLOCKED |
| V02 | Main window | 1240×760 standard and 1000×650 compact, no overlap | BLOCKED |
| V03 | High DPI | 100%, 125%, 150%, 200% screenshots, no clipped content, controls keyboard reachable | BLOCKED |
| V04 | Responsiveness | 1120 DIP breakpoint, compact library/editor with Back navigation and always accessible Start/Save | Logic unit tests; WPF BLOCKED |
| V05 | Settings | Six groups visible/scrollable, independent folder paths and toggles, Cancel/Save/Restore correct | BLOCKED |
| V06 | Accessibility | Focus order, screen reader labels, keyboard trim/volume, system high contrast and reduced motion | Static CI; Windows BLOCKED |
| V07 | Frost | Real transparency only where supported; opaque fallback; no continuous blur/refractive process | Static CI; Windows BLOCKED |
| C01 | Manual capture | Off on every app/Windows startup; source selection, Start/Stop hotkey works | BLOCKED |
| C02 | Rolling buffer | Requested duration vs actual retained 15–120s, 256 MiB bound and UI warning | BLOCKED |
| C03 | Moving footage | 720p30/1080p30/1080p60 NVENC; watch real game, no black/static/frozen frames, inspect FFprobe/decodes | BLOCKED |
| C04 | Source changes | 20 monitor/window changes, clear consent and no unwanted screen fallback | BLOCKED |
| C05 | Stress | 50 start/stop cycles, unique/immutable saves, no orphan workers or handle growth | BLOCKED |
| N01 | Branded notification | Full Flick squircle, Clip Saved only after verified indexed MKV, actual clip details | BLOCKED |
| N02 | Overlay/audio toggles | On/On, On/Off, Off/On, Off/Off, persisted, no false success chime | BLOCKED |
| N03 | Game focus | Non-activating click-through, ~2s, borderless/exclusive behavior honestly recorded | BLOCKED |
| L01 | Import | Existing MP4/MKV folders in place, no source copying/deleting, no outside traversal | BLOCKED |
| L02 | Big library | 100/1000/5000 clips; bounded scan, cancellation, responsive search/virtualization | BLOCKED |
| L03 | Offline drive | Library entries persist through unplug/restart and recover on rescan | BLOCKED |
| L04 | Ownership | Imported Remove/Unlink preserves originals and exports; Flick Delete retains explicit Recycle Bin behavior | BLOCKED |
| E01 | Editor | Native Original MP4/MKV playback; on-demand compatibility preview and cancellation | BLOCKED |
| E02 | Trim | Two handles, keyboard precise bounds, non-destructive, saved selection | BLOCKED |
| E03 | Audio | Real, independent streams where they exist; audible system/mic; mix/mute/gain and no doubled tracks | BLOCKED |
| S01 | Export | 20 MB default, 10/50/100/custom 1–250 decimal MB; H.264/AAC, stereo, exact bytes, full decode | BLOCKED |
| S02 | Clip identity | Two same-named source clips produce distinct MP4 outputs; no stale/overwritten Ready state | BLOCKED |
| S03 | Discord drag | Real MP4 file via OLE, recognized in Discord desktop composer **without sending** | BLOCKED |
| S04 | Clipboard | Copy File -> paste actual MP4 into Discord composer (no auto-send) | BLOCKED |
| S05 | Cancel/robustness | Cancel export, no orphan FFmpeg, incomplete file not marked Ready, originals unchanged | BLOCKED |
| S06 | Webhook | Disabled by default, DPAPI secret, separate consent to send, no auto-post | BLOCKED |
| P01 | Resource sampling | Five-second app/host RAM/CPU/handles CSV across test conditions | BLOCKED |
| P02 | GPU and FPS | Matched scene A/B, encoder/GPU VRAM, average/1% lows, p95/p99 frametimes | BLOCKED |
| P03 | Overlay overhead | ≤2 MiB hidden, ≤15 MiB shown target and under 1% FPS effect | BLOCKED |
| P04 | Endurance | 2h personal, 8h release soak after owner permission, no rising worker count | BLOCKED |
| R01 | User delivery | Portable zip or installer starts full GUI, correct resources/checksums, licensing/notices | BLOCKED |
| R02 | Privacy/permissions | No accounts/telemetry/uploads, mic opt-in, exact user-selected folders, no process injection | BLOCKED |
| R03 | Handoff truth | Complete PASS/FAIL/BLOCKED table for current SHA with reproducible evidence | BLOCKED |

## Evidence package required

1. Exact branch/commit, PC OS/GPU/CPU/RAM, driver version, dependency pins, build commands and full terminal/test logs (TRX).
2. Screenshots of **Clips, Editor with loaded video, all six Settings categories, expanded audio mixer, import list, disabled/offline states, Clip Saved banner**, and actual Discord composer recognition without sending. Label width/height and display scale for each.
3. Real MKV/MP4 test file sizes and SHA-256 before/after (no upload of personal videos), FFprobe streams/codec/fps/duration, motion detector and visual/audio inspection notes.
4. `scripts/sample_flick_performance.ps1` output separated by phase, and trusted GPU/FPS/frametime traces; don't conflate WPF resource metrics with FPS.
5. All process/fault/cleanup and soak observations, plus each defect's fix commit and regression retest. Preserve local evidence privacy.
6. Source and legal inspection for packaged app, temporary preview cache cleanup, licensing/dependency attribution and current README/user guide.

**Exit rule:** Do not recommend merge or public release until all critical functional, privacy and media gates PASS. Noncritical BLOCKED rows require explicit owner sign-off; no silent acceptance.
