# Flick GUI Slice 6 — Final Visual, Accessibility & Performance QA

**Draft PR:** https://github.com/Krish-Verma/Flick/pull/7  
**Branch:** `feat/gui-slice6-visual-performance-qa-20261020`  
**Base:** GUI Slice 5 `e97cb2942bb9071159986c63d329c209ba93ae87`, Windows CI https://github.com/Krish-Verma/Flick/actions/runs/39513312108 (100 offline passed, 7 GPU skipped, native/WPF builds green).  
**Status:** development and static CI qualification only. **Real Windows UI/GPU/Discord qualification must be performed by AntiGravity.** Keep repo private, PR draft, no merges/releases.

## Goal

The final GUI slice is a **polish and acceptance** slice, not another product expansion. Flick remains a free, offline-first Windows 11 game clipper with native WPF/.NET, libobs capture, FFmpeg export and a compact, muted graphite/pink-violet identity around the user's **full-squircle original logo**. Preserve every implemented feature from Slices 1–5.

## Implemented code contract

### Layout and visual consistency

- `WorkspaceLayoutRules` is the single testable policy: normal side-by-side library/editor at **1120+ WPF device-independent pixels**; below 1120 use a **compact one-pane workspace** so text, timeline and player are not squeezed into a narrow column. There are still only two main navigation destinations: Your Clips and Settings.
- In compact mode, **Your Clips** navigation shows the library; selecting a clip shows its editor; **← Your Clips** returns to the library. The capture bar still offers source selection, Start/Stop Replay and Save Clip with the actual duration in the tooltip. No capture state or editing data changes merely from a viewport resize.
- Main window minimum reduced to **720×480 DIPs**, Settings minimum to **620×460**, linked-folders dialog to **510×380**, with internal editor/settings scroll. At normal width Flick preserves the existing virtualized library/editor/grid splitter and sticky Discord export dock.
- Legacy MainWindow implicit controls now inherit the **same shared Segoe typography and button/input tokens** as the rebuilt panels. Logo colors stay concentrated in the logo and restrained focus/highlight accents. User-generated clip filenames are never artificially title-cased.
- Desktop WPF still requires **actual screenshots and input testing** for overlap, scroll/focus behavior, mixed DPI and long strings. A successful compile/static XML parse does not certify pixel-perfect layout.

### Accessibility and low overhead

- `FlickVisualPreferences` is a **single app-owned event subscription**, not a loop or process. It overrides named WPF brush tokens when Windows high contrast is enabled, and restores normal resources when disabled. It observes Windows transparency preference and won't attempt Mica when transparency is off. All changes require Windows verification.
- The temporary full-logo Clip Saved overlay respects reduced-motion/high-contrast preferences: no fade when animations should be disabled. Display lifetime (~2 seconds), independent sound/overlay settings, focus-safe click-through and no resident overlay process remain unchanged.
- `FlickBackdropService` still uses native DWM Mica where supported and allowed; other systems use solid resources. It adds no refractive pixel shader, continuous desktop capture or heavy frontend runtime.
- Source code and WPF geometry are unchanged for the libobs, NVENC and FFmpeg backend. UI polish must not add material FPS/latency overhead.

### Repeatable automated QA

- `scripts/check_gui_slice6.ps1` parses critical XAML, checks adaptive layout and focus-safe interaction contracts and computes **WCAG contrast ratios** from the actual named Flick color tokens. All five important text/background combinations must remain at least **4.5:1**. This is a static check, not proof of readability at every system appearance mode.
- `tests/LocalClip.Core.Tests/WorkspaceLayoutRulesTests.cs` verifies responsive breakpoints, logical minimum-size envelope and actual Save Clip label/meaning. No Windows-only test is marked passed on GitHub-hosted runners.
- `scripts/sample_flick_performance.ps1` collects process working set, private bytes, handle count, threads and CPU time at five-second or slower intervals for a named Flick app/capture-host PID. It **does not** measure game FPS, GPU/VRAM or export quality; those must come from real PC tools.
- `scripts/capture_flick_visual_evidence.ps1` explicitly requires a visible **foreground Flick-owned window** and a user keypress for each PNG. It refuses unrelated processes and makes no screenshot uploads. Screenshots are local evidence; DPI, overlapping windows, modal dialogs and media visuals must still be inspected manually.
- PowerShell syntax, static WPF contracts and offline unit tests are run by CI. WPF portable publish/native libobs compilation remain mandatory.

## Measured evidence versus engineering targets

Prior owner/AntiGravity reports before the full GUI rebuild indicated ~48.2 MiB idle app working set, ~142.5 MiB app+capture-host working set while buffering, 1.1–1.8% active capture CPU, ~185 MiB GPU VRAM allocation and 0.42s replay save latency. **These are historical backend figures, not current Slice 6 measurements.**

### Owner-facing qualification targets (not current passes)

| Measurement | Target / interpretation |
|---|---|
| App idle working set | ≤120 MiB, measured with library closed and capture Off |
| Combined app+host working set buffering | ≤350 MiB for normal profile |
| Default capture private bytes | Retain prior core guardrail ≤600 MiB |
| Clip Saved notification hidden overhead | ≤2 MiB incremental RAM and effectively zero continuous CPU |
| Temporary notification overhead | ≤15 MiB incremental RAM |
| Capture game FPS effect from new GUI/overlay | Under 1% average FPS relative to equal capture+no overlay A/B; inspect 1% lows/p95/p99 |
| Source import/editor idle CPU | No continuous scan/decoder when not requested |
| Resource/handle trend | Stable under 50 start/stops, 20 source changes, repeated editor/export sessions |
| Gameplay/soak | 2h personal and 8h release durations when authorized; BLOCKED if not run |

Do not add working set, private bytes and VRAM as though they measured the same resource. The resource sampler does not itself supply FPS. For FPS use the same game/scene/settings with capture and notifications controlled and a trusted external frametime capture.

## Six-pass QA record

1. **Visual:** shared fonts/control sizes, Title Case for authored actions, clipping, empty/long library titles, split/compact transitions, export dock never hidden.
2. **Accessibility:** Tab/Shift+Tab/Space/Enter/Escape, visible keyboard focus, precise trim handles, high contrast, reduced motion, disabled/notification states.
3. **Workflow:** manual start/save, tray, saved notifications, linked folders, offline clips, Original/Export preview, trim/audio, real MP4 share drag/copy.
4. **Resilience:** low disk/permissions, corrupt metadata, unexpected source close, FFmpeg job cleanup, no loss/overwrite of source media, no stale Ready exports.
5. **Resources:** app/host private/working sets, CPU, GPU/VRAM, FPS/frametimes, no hidden background decoders, repeated overlays and long-running stability.
6. **Documentation/release:** raw commands/evidence, each acceptance case PASS/FAIL/BLOCKED, pinned dependencies/third-party notices, current user guide, no published release or merged PR without authorization.

## Non-goals and remaining gates

Slice 6 does **not** implement AI highlights, social/cloud features, a full video editor, new game injection, Electron, new processes, forced fullscreen overlays, or automatic Discord posting. It does not turn Flick public or create a v1.1 release.

Execute **`docs/GUI-SLICE6-ANTIGRAVITY-HANDOFF.md`** and **`docs/GUI-WHOLE-PRODUCT-ACCEPTANCE.md`** on the owner's Windows/NVIDIA PC. Record logs/PNGs/benchmarks under a local excluded evidence folder, not as personal media in this repository. PR #7 remains draft until hardware review, screenshots and owner approval.
