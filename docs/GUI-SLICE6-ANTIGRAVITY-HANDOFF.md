# Flick Slice 6 — Consolidated AntiGravity Windows Execution Handoff

## Mission

Qualify **all six UI slices and underlying capture/export backend end-to-end** from the current private Flick draft branch. Fix source defects in this branch and rerun every impacted gate, then deliver raw evidence and a release-readiness recommendation. Do not stop after only building the app or uploading a ZIP.

**Repo:** https://github.com/Krish-Verma/Flick  
**PR:** https://github.com/Krish-Verma/Flick/pull/7  
**Branch:** `feat/gui-slice6-visual-performance-qa-20261020`  
**Base:** prior GUI Slice 5 PR #6 at `e97cb2942bb9071159986c63d329c209ba93ae87`.

**Strict boundaries:** Repository stays PRIVATE. No PR merges, v1.0.0 replacement, release publishing, auto-recording, webhooks actually posting, accounts/cloud, unapproved recordings upload, game injection, changing drivers, rebooting, deleting personal source clips or killing unrelated processes. Confirm before any destructive system change. All raw game/media tests must use explicit owner-authorized test clips/PC paths.

### Mandatory full reading

- `ClipperBlueprint/WINDOWS-CLIPPER-BLUEPRINT.md`
- `docs/GUI-WHOLE-PRODUCT-ACCEPTANCE.md` — **fill every row**
- `docs/GUI-SLICE6-IMPLEMENTATION-V3.2.md`
- `docs/GUI-SLICE1-ANTIGRAVITY-HANDOFF.md` (if present)
- `docs/GUI-SLICE2-ANTIGRAVITY-HANDOFF.md`
- `docs/GUI-SLICE3-ANTIGRAVITY-HANDOFF.md`
- `docs/GUI-SLICE4-ANTIGRAVITY-HANDOFF.md`
- `docs/GUI-SLICE5-ANTIGRAVITY-HANDOFF.md`
- `docs/ARCHITECTURE.md`, `docs/USER-GUIDE.md`, root README, `THIRD-PARTY-NOTICES.md`
- Latest CI/workflow, app/Core/capture-host code and every test.

## Phase 1 — Build and static tests

1. Clone/checkout **latest PR #7 HEAD** in a clean isolated Windows worktree, never modify ancestors PR #1–6.
2. Run in PowerShell: `./scripts/check_gui_design.ps1`, `./scripts/check_gui_slice2.ps1`, `./scripts/check_gui_slice3.ps1`, `./scripts/check_gui_slice4.ps1`, `./scripts/check_gui_slice5.ps1`, `./scripts/check_gui_slice6.ps1`.
3. `dotnet build LocalClip.slnx -c Release --warnaserror`; `./scripts/run_tests.ps1`; on the owner's NVIDIA PC also `./scripts/run_tests.ps1 -Hardware`. No count of skipped hardware tests as PASS.
4. Build native C++ host and self-contained portable Windows package with existing scripts; extract into a new path containing Unicode/spaces. Verify real WPF app starts, logo resource packaged, correct project/version/checksums.
5. Capture exact SHA, full logs, native deps/OBS module load, .NET test TRX and run links. Fix source errors and repeat.

## Phase 2 — Visual review and responsive UX

6. Take **real screenshots** at desktop 1240×760 DIPs, minimum 720×480 DIPs, around 1000×650 DIPs and 1120 breakpoint both sides. Record Windows DPI **100, 125, 150 and 200%**; inspect all screens and long titles/paths/labels and 5+ audio tracks. Use `scripts/capture_flick_visual_evidence.ps1 -AppPid <actual Flick PID>` for consented local PNGs; it only accepts the foreground Flick-owned window and does not upload evidence. Manual screenshots are fine when this helper cannot capture a modal or DWM surface correctly.
7. In compact view select a library clip -> editor; Back to Your Clips -> library. Start/Stop/Save remain visible with correct hotkeys; switching layout must not destroy a running buffer or trim selection. In split view check resizable left list/player. Sticky export dock must stay usable without overlapping the video and its controls.
8. Verify all six Settings categories: Recording/Audio/Shortcuts/Storage/General/Sharing. Cancel, staged Save, Restore Defaults, keyboard navigation, Tab/Shift+Tab/Enter/Escape, visible focus, pointer/touch scaling, cursor/hit targets and accessible labels.
9. Toggle Windows **High Contrast**, Reduce Motion/animation and Transparency Effects; verify WPF colors update, legible text and clear focus, Mica/blur falls back safely and no transition animations when reduced motion. Inspect actual render (CI only checks code/static contrast). Capture which settings are supported by the OS.

## Phase 3 — Complete real clipper walkthrough

10. Launch with capture OFF, choose an exact game/window/monitor, hotkey arm, play real **high-motion gameplay** 30s+, save. Verify actual MKV frames visually and via FFprobe/full-decode/motion checks, accurate timestamp and Clip Saved banner/sound only after indexed success. Repeat with overlay/sound all four combinations. Test focus in borderless/exclusive fullscreen without hooks.
11. Save 50 times over controlled sessions; start/stop 50 cycles, switch capture source 20 times with clear confirmation, and ensure no older MKV is renamed as the latest, no UI false positive, no orphan host/encoder.
12. Test imported MP4/MKV folders from another local clipper and at 100/1000/5000 media sizes, offline USB/removable drive, rescan/cancel, source ownership and imported-file hashes. No automatic copying/uploads, no path traversal outside chosen folders.
13. Preview original, seek, build bounded MKV compatibility preview only on request, edit two-handle trim and audio gain/mute, verify original SHA-256 unchanged and selected edits persisted. Check keyboard, reset, short trims and correct track identities/audibility.
14. Export actual Discord-ready 20 MB (default) H.264/AAC stereo MP4; 10/50/100/custom sizes, bytes never exceed cap. Test cancel/retry; no corrupt file Ready. Drag actual MP4 into **real Discord desktop composer** and Copy File -> paste, **do not send**. Verify directory changes invalidate old-folder Ready, named collisions don't overwrite, stale/changed/missing output rejected.
15. Test Windows startup/tray disarmed, optional mic off, permissions, disk full, corrupt JSON index/settings, preview cancellation on recording, Clip Saved notification no focus stealing and disabling webhook keeps local sharing entirely functional. In an isolated test profile, put malformed `settings.json` on disk: Flick must start disarmed with safe in-memory defaults, display a recovery warning and preserve the original damaged JSON bytes **both before and after the startup capture-source list is populated and after an incidental source selection**. Then test a parseable profile with ReplayDurationSeconds=0 and DiscordTargetSizeMb=0: the UI must avoid exceptions and expose safe defaults without silently modifying the file. User-initiated Settings Save is the sole path that commits recovery. Restart with a saved window source whose app is closed: Flick must not auto-switch to a monitor or arm via hotkey until a user explicitly chooses an available source. Check capture badge and fallback player text in both light and dark Windows high-contrast themes.

## Phase 4 — Real resource measurements and endurance

16. Use `scripts/sample_flick_performance.ps1 -AppPid <Flick PID> -HostPid <capture host PID> -Phase <phase> -DurationMinutes 5 -SampleSeconds 5` separately under Idle, Buffering, NotificationOn, NotificationOff, Library, Editing, Export and Soak. Save local CSV evidence; no helper processes or background encoder/preview expected at idle.
17. Independently measure actual **game FPS, 1% lows, p95/p99 frame-times** and GPU/VRAM/encoder usage in a fixed scene with notifications disabled/enabled, capture off/on and before/after GUI changes. Make claims only from actual measurements; no tool-generated FPS estimates.
18. Compare working set, private bytes, CPU/handles against documented engineering targets and historical pre-GUI baseline. Test 2-hour personal-use and 8-hour release soak if owner authorizes; otherwise mark **BLOCKED** with rationale. Measure leaked handles/PIDs, crash/restart, quit from tray, no orphan capture/FFmpeg.
19. When glass/animations cause measured overhead or accessibility failures, prefer opaque/natively composited surfaces and simpler transitions over new shaders. Preserve the full Flick logo and tiny notification.

## Phase 5 — Final evidence and handback

20. Update `docs/GUI-WHOLE-PRODUCT-ACCEPTANCE.md` with one PASS/FAIL/BLOCKED row per ID, exact current commit, screenshots, commands, file hashes, frame-time and RAM reports. Fix failures and requalify; update implementation docs and user guide only to reflect observed functionality.
21. Supply final private PR link, built portable package location and SHA-256, WPF screenshots (reviewed for overlap), evidence/log locations, tested game/media fixtures, both CI and Windows hardware tests, explicit regressions and blockers. Include concise recommended next steps and whether public v1 release is safe. Do not invent results.
22. **STOP at owner review.** Do not merge any of the six draft GUI PRs or PR #1; do not publish a release or start new features automatically.

**A compile-only or static UI result is not final UI approval.** All hardware-dependent, DPI, drag/paste and full-screen focus outcomes are genuinely required.
