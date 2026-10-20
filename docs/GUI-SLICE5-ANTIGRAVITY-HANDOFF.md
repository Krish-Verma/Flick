# Flick GUI Slice 5 — Windows Acceptance Handoff

**Task:** On the user's Windows RTX 5060 PC, test the actual WPF UI and fix any discovered defects in **draft PR #6**. Code is not finished merely because it compiles.

- Repo (private): https://github.com/Krish-Verma/Flick
- PR: https://github.com/Krish-Verma/Flick/pull/6
- Branch: `feat/gui-slice5-sharing-settings-20261020`
- Read: `docs/GUI-SLICE5-IMPLEMENTATION-V3.2.md`, `docs/GUI-SLICE4-ANTIGRAVITY-HANDOFF.md`, `ClipperBlueprint/WINDOWS-CLIPPER-BLUEPRINT.md`, `docs/USER-GUIDE.md`, all relevant source/tests.

## Build and data safety

1. In a clean isolated PR #6 worktree, inspect source commit and verify latest CI. Run `scripts/check_gui_design.ps1`, `scripts/check_gui_slice2.ps1`, `scripts/check_gui_slice3.ps1`, `scripts/check_gui_slice4.ps1`, `scripts/check_gui_slice5.ps1`, `dotnet build LocalClip.slnx -c Release --warnaserror`, `scripts/run_tests.ps1`, `scripts/run_tests.ps1 -Hardware`. Rebuild native capture and a fresh self-contained Windows portable package. Hardware skipped in cloud CI = SKIPPED, NOT PASSED.
2. Back up user settings/index and use test media/folders. NEVER delete/move personal recordings, post to Discord, change drivers, reboot, merge PRs or publish a release without owner consent.

## Compact export and real Discord drag

3. Start from manual capture Off. Save a genuine 30-second moving-gameplay MKV with audible system/mic tracks. Trim and mix using Slice 4 UI, then export to **20 MB default**. Verify exact bytes <=20,000,000 and full H.264/AAC stereo probe and decode; also test 10, 50, 100 and Custom (1, 17, 250 MB) as applicable. Confirm the app does not claim these limits guarantee Discord acceptance; actual channel's upload cap may differ.
4. Inspect compact sticky export dock while no clip selected, offline clip, importing, exporting, failed, cancelled and Ready. Only available clips enable Export. The progress strip must indicate ongoing work without pretending a staged percentage is real elapsed time; actual stage label must be legible. Start a longer export, click **Cancel Export**, ensure the owned FFmpeg process exits and no corrupt temp is promoted; verify original hash and previously valid MP4 are unchanged.
5. When Ready, press (without moving) the draggable MP4 tile: no drag should occur. Move beyond Windows drag threshold: an actual Windows OLE FileDrop drag should start with the exported MP4, not virtual text or a shortcut. Drag it into the **real Discord desktop composer**, verify the destination recognizes it; **do not send the message**. Test Copy File -> Paste into Discord and Open Folder. No account credentials are required by Flick.
6. Rename/delete the validated MP4 externally, change trim/audio, change size cap, select another clip, or overwrite the ready file with a different byte count. Flick must refuse to share stale/missing/invalid data rather than silently presenting a ready file. Export a clip, switch selected library item before it finishes; completion must never mark the *other* clip Ready or alter that clip's metadata.

## Settings

7. Open Settings from navigation and tray. Check exactly **six groups**: Recording, Audio, Shortcuts, Storage, General, Sharing; consistent Segoe type, Title Case, 36/40px control heights, focus, muted gradient branding and proper section scroll at 1000×650, 1240×760, 100/125/150/200% scaling. Verify Cancel and Restore Defaults leave saved settings unchanged until explicit Save.
8. Set duration/Light-Balanced-Smooth presets, confirm displayed memory budget and effective next-session settings; while capturing, merely opening Settings must not disarm. Disabling an active microphone must ask permission and actually stop/release it; Cancel must not disconnect devices.
9. Test shortcut capture fields with Ctrl+Shift+F9/F8 and alternative chords; reject duplicated or reserved shortcuts without losing previous functioning global hotkeys. Verify hotkeys from inside real games when tray-only.
10. Change Saved Clips and Export folders independently to two Unicode/spaces paths. Save, restart, clip and export; confirm new files go to the right folders, old files unchanged. Invalid paths and read-only disk failures must be surfaced; no silent migration. Import/connected folders remain functional.
11. Test Windows startup and minimized options, both Clip Saved Overlay and Confirmation Sound toggles, and Preview Notification. Ensure defaults on, independent toggles, no capture auto-start, original Flick logo. Check the temporary non-activating overlay doesn't steal game focus.
12. In Sharing choose 10/20/50/100/Custom. Save, cancel and restart; confirm persistence and the main export dock updates. The optional webhook must default disabled, remain collapsed, use DPAPI secrets, require explicit enable and a separate **per-send** confirmation. Do NOT actually send a clip. Check that disabling webhook still allows normal offline MP4 exporting and clipboard/drag sharing.

## Regression and performance

13. Retest Slice 4 Original/Export Preview, precise trim, audio gains/reset, stale preview invalidation, on-demand local MKV fallback; test no extra worker while minimized. Retest Slice 3 library import/unlink/disconnect and Slice 2 tray/notification. Record true PASS/FAIL/BLOCKED and fix defects in this branch.
14. Compare app working set, private bytes, CPU/GPU/VRAM and game FPS/1% lows with prior measured backend baseline. Check no extra always-running components from grouped Settings or export dock, no persistent graphics blur, no visible clipping/overlap/overflow. Capture real screenshots of small/normal/high-DPI layouts and keyboard accessibility.

## Delivery rules

Return commit SHA, CI links, Windows build steps, raw test logs/TRX, file hashes, ffprobe/full-decode outputs, screenshot evidence, real Discord composer acceptance, cancellation-process evidence and an explicit acceptance matrix. If a gate cannot be physically checked mark BLOCKED. Fix failures, push to PR #6, rerun CI and update `docs/GUI-SLICE5-IMPLEMENTATION-V3.2.md` and this handoff with actual evidence.

Do not merge, upload personal media, make repo public, release v1.0.0, or begin Slice 6 without owner approval.
