# Flick GUI Refresh R1 — AntiGravity verification handoff (TEST ONLY)

Repository: Krish-Verma/Flick
Branch: feat/gui-refresh-r1-single-window-20261023
PR: linked from GitHub branch / pull requests. Record exact checkout HEAD and CI URLs first.
Read docs/GUI-REFRESH-2026-MASTER-PLAN.md and docs/GUI-REFRESH-R1-IMPLEMENTATION.md in full before testing.

**You are a tester. Do not edit/push code, scripts, tests, documentation, GitHub branches, PRs, releases, visibility or security settings.** Return isolated reproductions and sanitized logs to the coding owner. Use synthetic/test-approved media; never upload personal footage, token or Discord chat messages.

## Source-build gate

On Windows 11 x64 developer environment with pinned tooling:
    git rev-parse HEAD
    git status --short
    ./scripts/check_gui_design.ps1
    ./scripts/check_gui_slice2.ps1
    ./scripts/check_gui_slice3.ps1
    ./scripts/check_gui_slice4.ps1
    ./scripts/check_gui_slice5.ps1
    ./scripts/check_gui_slice6.ps1
    ./scripts/check_gui_refresh_r1.ps1
    ./scripts/check_flick_release_source.ps1
    dotnet restore LocalClip.slnx
    dotnet build LocalClip.slnx -c Release --warnaserror
    ./scripts/run_tests.ps1
    ./scripts/run_tests.ps1 -Hardware

Preserve raw CLI output. Assert 120 offline pass and 7 hardware skips on hosted runner are different from real GPU test results.

## One-window navigation matrix

1. Startup shows **one Flick native application window**. Click Settings: the main central pane switches to Settings, while the capture bar, status and nav rail remain. Verify no extra Flick top-level window in Task Manager/Alt+Tab.
2. Inspect all six settings categories. Change replay duration, shortcuts, storage and other fields without pressing Save; click Cancel or Clips. Reopen and verify unchanged persisted values. Save a different valid setting; verify file, UI and registered hotkeys reflect exactly the saved setting, with no partial write.
3. Settings -> Storage -> Manage Folders -> Close returns to Settings with staged edits intact. Clips -> Import Folder -> Close returns to Clips. Use keyboard-only Tab/Enter/Escape/scroll. Smallest window, split/compact, 100/125/150/200% scale.
4. Add and rescan a temporary MP4/MKV source through native Windows folder picker. Confirm link + thumbnail appears, unlinks never delete originals/exports, missing external drive retains offline metadata. Cancel an in-progress long scan and navigate: no orphan/duplicate scan or broken UI, active game capture unaffected.
5. Select a clip and play it; navigate to Settings. Video decoder unloads and no FFmpeg background preview survives. Return to Clips: same selection, trim/audio choices and poster, initial playback remains stopped. Repeat while FFmpeg is generating a local preview, under narrow and wide layouts.
6. With NVENC replay active on RTX GPU, navigate through Settings/Folders, change mic opt-out then Save, verify mic release and correct replay stop behavior; otherwise capture continues. Starting from disarmed state never auto-arms. Uninstall opens the Windows registered uninstaller only after explicit user confirmation.
7. Hide to tray while viewing Settings/Folders; restore via tray. Verify no extra Settings window, no hidden player decoder, single hotkey listener, no repeated tray icons, normal Quit cleanup.
8. Confirm capture sources, Save Last Replay hotkeys, library selection, export format, Discord drag/copy and notification settings have not regressed. Do not send Discord messages.

## Performance and evidence

Capture before/after baseline using the exact previous main build and this R1 source with matching fixtures and conditions. Collect app/private WS, CPU, handles, GPU/VRAM, idle Settings page, selected clip, preview and scan states. Test actual gaming FPS/1% lows with replay off/on and show any regressions; no invented performance claims. Screenshots at full resolution and different Windows scaling, including high-contrast. Record playback startup latency and number of app-owned top-level windows.

Final report: one PASS / FAIL / BLOCKED gate table with exact commit, commands, artifacts, photos/screenshots and non-sensitive logs, failure steps and GO/NO-GO for **R1 only**. Hardware and endurance not performed must remain BLOCKED. Coding owner implements all fixes after QA.
