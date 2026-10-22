# Flick — Post-Slice-6 Installer, Open-source and RAM Qualification (AntiGravity TEST ONLY)

**Repository:** https://github.com/Krish-Verma/Flick (currently PRIVATE)
**PR:** https://github.com/Krish-Verma/Flick/pull/8 (DRAFT)
**Branch:** feat/flick-installer-open-source-performance-20261021
**Base:** GUI Slice 6 PR #7 commit fd937f196b3f0ece75b7c77c639e2bca39c15d05
**Head:** At execution, record fresh git rev-parse HEAD, not a hard-coded commit.

## Critical ownership rule

**AntiGravity is responsible for testing, benchmarking and reporting ONLY.** Do not modify any source, PowerShell scripts, installer, tests, documentation, workflow, settings templates, GitHub branches, commits or PR metadata. Do not cherry-pick, merge or publish. If a defect is found, record exact reproduction, log, stack trace and screenshot (if safe), classify severity and return it to the coding owner. **The coding owner will implement every fix.** AntiGravity may run local temporary test harness commands and collect evidence but must not commit modifications.

Use only synthetic fixtures, explicit owner-approved media and isolated test profiles. Never upload personal clips, private capture sources, keys/webhook secrets or desktop screenshots containing private content to GitHub. Do not send messages to Discord. No forced reboot/driver changes, no broad process kills or deletion of user video files.

## Mandatory reference documents, read in full

1. README.md, CONTRIBUTING.md, SECURITY.md, LICENSE and LICENSE-NOTICE.md, THIRD-PARTY-NOTICES.md.
2. docs/INSTALLATION-DISTRIBUTION.md (safe data migration and installer paths).
3. docs/PERFORMANCE-OPTIMIZATION-IMPLEMENTATION.md (each RAM change, targets and A/B).
4. docs/OPEN-SOURCE-RELEASE-CHECKLIST.md (release gates and licensing).
5. docs/GUI-WHOLE-PRODUCT-ACCEPTANCE.md, docs/GUI-SLICE6-ANTIGRAVITY-HANDOFF.md, and the existing GUI Slices 1–5 QA handoffs. Their prior hardware results are a comparison baseline, not substitutes for PR #8 tests.
6. installer/Flick.iss, scripts/build_flick_icon.ps1, scripts/build_package.ps1, scripts/build_installer.ps1, scripts/check_flick_release_source.ps1, .github/workflows/ci.yml, .github/workflows/preview-installer.yml.
7. src/LocalClip.App/Services/ThumbnailImageConverter.cs, src/LocalClip.Core/Media/MediaProber.cs, src/LocalClip.App/MainWindow.xaml and .xaml.cs, src/LocalClip.App/LocalClip.App.csproj and src/LocalClip.App/Services/WindowsStartupRegistration.cs.

## Phase 0 — Clean environment and provenance

- Check out latest PR #8 HEAD into a **separate Windows worktree**, preserving the existing PR #7 and unrelated tasks. Record git status --short and git rev-parse HEAD. Do not accidentally checkout main, which lacks the stacked GUI.
- Record Windows version, Ryzen CPU/GPU, NVIDIA driver, memory, actual DPI and mixed monitors, build tool versions including .NET SDK, MinGW compiler, Inno Setup version and full binary hashes.
- Establish non-admin standard user account and isolated directories for screenshots, fake settings/indexes/media fixtures and installer tests. Do not use real data for deletion/uninstall experiments.
- Back up any existing **%LOCALAPPDATA%\LocalClip** profile before attempting an upgrade.

## Phase 1 — Static gates, build, and test

Run in PowerShell, preserving raw logs and exact result counts:

~~~powershell
git rev-parse HEAD
git status --short
./scripts/setup_tools.ps1
./scripts/check_gui_design.ps1
./scripts/check_gui_slice2.ps1
./scripts/check_gui_slice3.ps1
./scripts/check_gui_slice4.ps1
./scripts/check_gui_slice5.ps1
./scripts/check_gui_slice6.ps1
./scripts/check_flick_release_source.ps1
dotnet restore LocalClip.slnx
dotnet build LocalClip.slnx -c Release --warnaserror
./scripts/run_tests.ps1
./scripts/run_tests.ps1 -Hardware
./scripts/build_package.ps1
./scripts/build_installer.ps1 -SkipPortable
~~~

- Pass source-only gates without confusing them for Windows hardware acceptance.
- Verify **Flick.exe**, original full-squircle **Flick.ico**, two packaged formats, bundled native CaptureHost, OBS and FFmpeg/FFprobe paths, all third-party notices and SHA-256 manifest.
- Use Inno Setup 6 ISCC.exe to build **Flick-Setup-1.0.0-win-x64-preview.exe** (unsigned), and record its hash, signature state, output file size and compiler logs. No code signing or release publishing.
- Run the native libobs/NVENC/FFmpeg tests on the owner's RTX 5060; distinguish passed/failed/skipped with raw output (not inferred from previous PR #7 report). No application should auto-arm after setup/launch.

## Phase 2 — Clean install, launcher, update and uninstall

On a standard non-admin account and preferably a clean Windows 11 VM:

1. Run **Flick-Setup-1.0.0-win-x64-preview.exe**. Verify familiar wizard, logo, UAC behavior (no elevation), install destination **%LOCALAPPDATA%\Programs\Flick**, proper Start Menu shortcut, optional desktop shortcut, Installed Apps entry and correct uninstaller.
2. Confirm Start Menu and desktop shortcuts launch the branded **Flick.exe**, not the old LocalClip.App.exe. It launches WPF/OBS dependencies, tray icon and exact saved status correctly, capture **OFF** until manual action.
3. Install with spaces/Unicode in the custom destination; verify the runtime discovers OBS, FFprobe/FFmpeg and original full-squircle logo. Test fresh settings, window source and startup toggle; startup Run key must point to actual Flick.exe and must remain opt-in.
4. Simulate upgrade from an **isolated old legacy profile**: seed settings.json, clips_index.json, connected_sources.json, Thumbnails, fake clip originals/exports and a linked external directory. Save SHA-256 of all test files. Install/upgrade/launch new Flick and ensure source lists, selection, original files and settings survive.
5. Shut down via tray **Quit** before another installer run; ensure running-instance policy doesn't terminate an active replay or overwrite open files. Test update/reinstall and uninstall through Windows Installed Apps.
6. After uninstall, compare SHA-256 of **all** seeded user clips, exports and profile files and verify old files are **byte-identical**. Confirm program files/shortcuts/uninstaller are gone, no registry startup entry silently created, no orphan capture host. Do NOT run the destructive historic LocalClip uninstall.ps1.
7. Test portable ZIP separately after extraction; run the included Flick.exe without any prior .NET runtime installed in that test environment. Close and remove only the portable app folder (not user data).
8. If unsigned Windows Defender SmartScreen/publisher warning appears, document exact wording and provenance/hashes; do not make unsupported claims about trust/certification.

## Phase 3 — Memory and complete WPF behavior

Use the previous full-GUI SHA on PR #7 as baseline and the new PR #8 HEAD, **same Windows conditions**, real app UI and identical libraries. Three or more independent samples per phase:

- Fresh/empty library, replay **Off**, window open for 5 minutes; collect idle WS/private bytes/handle/thread/CPU.
- Library of 50/1000/5000 clips using synthetic images/MP4 and both previous full-resolution JPEGs and new 320x180 bounded JPEGs. Compare after warm-up, scrolling, search, and idle. Inspect actual generated thumbnail dimensions and image-file handles.
- Selecting a clip must NOT silently open MediaElement decoder/FFmpeg; show poster. Test Play, pause, seek, trim, original/export preview, failed/MKV fallback, clip switching, max-duration clips, offline/disconnected drive, reselect and file handles. Observe first-play latency, image quality and memory.
- Hidden-to-tray and minimized must release video source/decoder; restore should show editor poster and preserve library/settings/trim. Verify optional sound/overlay still function with the exact logo.
- Sample resource usage with **scripts/sample_flick_performance.ps1** in each phase (pass actual AppPid and HostPid), 5-second interval, raw CSV, same clips, size and exact current commit. FPS requires external profiler; do not derive it from CSV.
- Preserve goal of <=120 MiB idle app working set as an aspiration and report measured improvement against historical 163.1 MiB. Never substitute a higher threshold without explicitly calling it an exception.

## Phase 4 — Game performance, privacy and usability

- Test recording via NVENC with actual explicit game/window capture on RTX 5060; Save Clip once and 50 times, source switches 20x, two-handle trim, original untouched, all audio channels, 10/20/50/100/custom MP4 caps, copy/drag into Discord desktop composer **without posting**.
- Record comparable average game FPS, 1% lows, p95/p99 frame times for same game scene across replay off/on, notification off/on and old/new GUI builds. Include GPU/VRAM and app/host working-set stats. Prove no automatic game capture or additional idle host.
- Windows screenshot audit at 100%, 125%, 150% and 200%, normal/compact editor, keyboard focus, High Contrast, transparency disabled and reduced motion. Capture via manual/window helper and inspect locally.
- Verify no account, telemetry, auto-uploads, spurious microphone permissions or destructive media import. Test settings recovery, missing capture source, missing thumbnails, cancel/retry and no orphan processes.
- Multi-hour 2h personal / 8h release soak ONLY with owner permission; if not executed, keep BLOCKED.

## Phase 5 — Final test-only handback

Provide a single test report **without changing source or PR files**:

- Exact source commit, portable ZIP and Setup EXE hashes and sizes, tools/build versions, CI URLs and full pass/fail/skipped counts.
- A table with **PASS / FAIL / BLOCKED** for every installer, memory, game/FPS, visual/DPI, privacy, source/license and endurance gate; cite concrete raw file paths and sample timestamps for every PASS. A screenshot at 100% cannot prove 200% DPI; NVENC throughput alone is not a game FPS measurement.
- For FAIL provide stack trace, minimal deterministic reproduction and proposed fix direction; do not implement it.
- Preserve private evidence locally and disclose only sanitized safe logs and paths. A GitHub PR comment containing a summary and redacted trace links is okay; no binary user media to the repo.
- Return final GO/NO-GO **recommendation** to the coding owner. Do not merge PR #8/7, sign, tag, publish GitHub Release, alter visibility, change data or create permanent system settings.

**AntiGravity tests. The development owner codes and documents all fixes.**
