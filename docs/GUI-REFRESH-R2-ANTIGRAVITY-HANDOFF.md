# Flick GUI Refresh R2 — AntiGravity Windows TEST-ONLY Handoff

Repository: Krish-Verma/Flick
Source: feat/gui-refresh-r2-themed-chrome-menus-20261023, stacked on R1 PR #16; report the EXACT HEAD SHA.
Read docs/GUI-REFRESH-2026-MASTER-PLAN.md and docs/GUI-REFRESH-R2-IMPLEMENTATION.md before testing.

**AntiGravity tests ONLY.** No code/docs/branches/PR edits or releases, no altering Smart App Control settings, no Discord posting, no uploading personal recordings. Return isolated, sanitized screenshots and actionable reproduction instructions.

## Build
Run scripts/check_gui_design.ps1; scripts/check_gui_slice2.ps1 through scripts/check_gui_slice6.ps1; scripts/check_gui_refresh_r1.ps1; scripts/check_gui_refresh_r2.ps1; scripts/check_flick_release_source.ps1; dotnet build LocalClip.slnx -c Release --warnaserror; scripts/run_tests.ps1; NVIDIA hardware tests on an approved Windows 11 system. Preserve logs.

## Windows visual & functional matrix

1. Capture the native Windows title bar in normal, maximized, restored and Snap Layout / Win+Arrow modes at 100%, 125%, 150%, 200% DPI. Confirm native minimize, maximize, close, resize and keyboard window interactions. The app caption should be graphite/dark, not the white strip in the original report.
2. With Windows high contrast and color-preference changes, ensure native caption and readable resources remain accessible. No fake titlebar/overlay stealing focus. If the compositor rejects a property, capture OS build and resulting state.
3. Inspect Flick icon in navigation, Settings and saved-clip notification at each DPI: original artwork centered within a real circle, aligned optically, no cropped internal glyph, stretched gradient, offset edge, or second decorative circle. Document any change in pixel spacing that needs developer correction.
4. List 0, 10, 100, 1000 and 5000 synthetic clips, scroll wheel, scrollbar thumb dragging, track/page clicking, keyboard PageUp/PageDown, clip selection. Use Settings, popup combo boxes, folder management and long editor pages. No bright OS bar; rail stays narrow and lists remain virtualized and responsive.
5. Open playback Original dropdown and clip ellipsis; confirm dark popup background, muted separator, lavender hovered row and keyboard focus. Use arrow keys, Enter and Escape; exercise actual Original, Export Preview, Open External Player, Delete/Unlink actions against synthetic data only. Note if any menu submenu/path fails.
6. Verify previous R1 single-window Settings and Connected Folders navigation; saved vs unsaved settings, active recording, microphone opt-out, clip selection and tray restore continue to work. WinForms tray may still follow Windows context menu visuals.
7. Capture baseline and R2 matching CPU/GPU %, working set/private bytes, handles, idle/replay buffering, library scroll, menu opening and playback start. Measure real FPS and 1% lows if approved; never invent missing results.

Report one PASS / FAIL / BLOCKED matrix with exact source SHA, screenshot/file hashes and environment, full log links, runtime resource measures and repro steps. Hardware tests not run are BLOCKED. Coding owner alone fixes defects. Keep GUI PR private and unmerged until acceptance.
