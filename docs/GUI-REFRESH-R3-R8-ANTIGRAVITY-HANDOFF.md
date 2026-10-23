# Complete Flick refresh — AntiGravity test-only handoff

## Candidate and roles

Repository: https://github.com/Krish-Verma/Flick (private).
Cumulative branch: `feat/gui-refresh-r8-save-feedback-20261023`, PR #23, stacked on #22 through #16. Check the live PR head and pin its exact SHA before testing; do not use the old preview download as the refreshed candidate. This branch's code, documentation and tests are owned by Codex. AntiGravity is **testing and reporting only**: no edits, commits, pushes, merges, publishing, actual Discord sends, personal-media uploads or security-setting changes. Use isolated synthetic clips/settings and authorized hardware. Do not bypass Smart App Control. If signature trust blocks launching a candidate, record BLOCKED and request a properly signed candidate.

Read the master plan and each R1–R8 implementation/handoff document, the original whole-product acceptance, performance plan and Windows trust documentation. Obtain the exact candidate's successful Windows CI and private packaging artifacts; verify hashes. Hosted CI builds the native host but cannot certify NVIDIA recording or physical screenshots.

## Required PASS / FAIL / BLOCKED matrix

| Area | Required evidence |
|---|---|
| R1 navigation | Clips → Settings → Folders → correct parent, staged Save/Cancel, active capture and mic opt-out, scan cancellation, selected clip/trim/audio state and tray restore |
| R2 styling | Actual native title bar, original circular logo, scrollbar wheel/drag/page/keyboard behavior, context menus, high contrast, Snap/resize, real screenshots |
| R3 layout | 720×480 minimum, typical desktop and 100/125/150/200% DPI; preview dominates, all four spaced icon labels and playback remain reachable, no overlap/clipping; empty/offline/playback-error state |
| R4 editing | Open/close/Escape and focus return; one drawer at a time; scroll long audio lists in small windows; precise trim input/keyboard/scrub, per-track mute/gain/reset, preserved state; compare original hashes |
| R5 Share | Opening Share never exports/posts; 10/20/50/100/custom 1–250 decimal MB; bad/empty/stale output unavailable; full FFmpeg decode; cancellation; close/reopen during export; native file copy/drag into Discord composer without sending; explicit destination consent for opt-in webhook (cancel only) |
| R6 shortcuts | Read-only muted saved chords; Change → Record → Save Shortcut → overall Save; Cancel/Tab/Escape/category switch/navigation; duplicate/modifier-only/Windows/reserved/OS-conflict rejection; existing registered shortcuts during capture; no trigger or persistence while editing; duration label follows setting |
| R7 prompts | Source-reset, mic-stop, defaults, unlink/import, delete vs imported-remove, rename, uninstall, webhook; No/Cancel initial focus; Escape and Tab cycle; capture continues during prompt; tray/hotkeys cannot bypass consent; cancel preserves buffer/media/settings; shutdown/minimize/reentrant event safety |
| R8 banner | A real confirmed save displays only `Clip Saved` and actual rounded duration (`30 sec`), no titles/IDs/numbers/filenames; same for preview/fallback; failure cannot emit success |
| R8 sound | Four overlay/sound combinations; Minimal/Quiet/custom local PCM WAV, preview/reset; ≤1 MiB and ≤3s bounds, invalid RIFF/chunks/codec/truncated/silent/oversized/overlong, missing/moved/unplugged files and network/link rejection; harmless default fallback; rapid saves coalesce; no startup file reads or disabled sound cache |
| Regression/performance | Real RTX/NVENC/audio/replay/save/import/trim/export; exact same fixtures versus Preview 2, empty/50/1000/5000 libraries; first-play latency, scrolling, stop/hide-to-tray cleanup, RAM/private memory/CPU/GPU/handles, gameplay FPS/1% lows/frame times; no invented deltas |
| Endurance/release | Owner-authorized soaks only; otherwise BLOCKED. Keep signing, corresponding-source/license and public-release approval separate |

## Reproducible source checks

Run on Windows with the repository-pinned toolchain:

```powershell
dotnet restore LocalClip.slnx
dotnet build LocalClip.slnx -c Release --no-restore --warnaserror
dotnet test LocalClip.slnx -c Release --no-build --logger "trx;LogFileName=Flick-Tests.trx"
./scripts/check_gui_design.ps1
./scripts/check_gui_slice2.ps1
./scripts/check_gui_slice3.ps1
./scripts/check_gui_slice4.ps1
./scripts/check_gui_slice5.ps1
./scripts/check_gui_slice6.ps1
./scripts/check_gui_refresh_r1.ps1
./scripts/check_gui_refresh_r2.ps1
./scripts/check_gui_refresh_final.ps1
./scripts/check_flick_release_source.ps1
```

Also run the documented hardware suite only on the authorized NVIDIA machine. Report offline, STA WPF staging and actual hardware results separately; skipped hardware tests do not pass. The WPF staging test constructs resources/Settings without launching the capture host and verifies cancellation does not commit edits; it is not a screenshot or keyboard-hardware test.

## Deliverable

One consolidated report: exact SHA/artifact hashes, test counts and logs, requirement-by-requirement results, actual screenshots, raw before/after measurements, unchanged original file hashes, defects with severity/reproduction steps, and GO/NO-GO for refresh acceptance. Code fixes return to Codex. Report source-verified versus physical-verified explicitly. No public release or signing enrollment is authorized by this handoff.
