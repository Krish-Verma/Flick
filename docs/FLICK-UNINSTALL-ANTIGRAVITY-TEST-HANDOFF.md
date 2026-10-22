# Flick v1.0.2 — Uninstall and Windows/NVIDIA Release Verification (Antigravity TEST ONLY)

## Ownership and source

Repository: https://github.com/Krish-Verma/Flick
Source PR: find the branch feat/flick-uninstall-release-readiness-20261022, check out the latest exact HEAD (record SHA).
Do not test old v1.0.1 binaries as though they contain the new uninstall feature.

**AntiGravity performs testing ONLY.** No source changes, no commits, no release publication, no changing visibility, no altering implementation files. Report defects, sanitized logs and reproduction steps to coding owner. Do not upload personal media or Discord secrets.

## Preflight and build

Read README.md, docs/INSTALLATION-DISTRIBUTION.md, docs/RELEASE-PROCESS.md, docs/OPEN-SOURCE-RELEASE-CHECKLIST.md, installer/Flick.iss and installer/PREVIEW-SYSTEM-REQUIREMENTS.txt. Compare the new version against v1.0.1-preview.1 and read the full existing GUI/hardware acceptance handoffs.

Run these commands from the checked-out Windows worktree:

    git status --short
    git rev-parse HEAD
    ./scripts/setup_tools.ps1
    ./scripts/check_gui_design.ps1
    ./scripts/check_gui_slice2.ps1
    ./scripts/check_gui_slice3.ps1
    ./scripts/check_gui_slice4.ps1
    ./scripts/check_gui_slice5.ps1
    ./scripts/check_gui_slice6.ps1
    ./scripts/check_flick_release_source.ps1
    dotnet build LocalClip.slnx -c Release --warnaserror
    ./scripts/run_tests.ps1
    ./scripts/run_tests.ps1 -Hardware
    ./scripts/build_package.ps1
    ./scripts/build_installer.ps1 -SkipPortable
    ./scripts/verify_flick_release.ps1

If installed MinGW/Inno environment differs, follow prior tested Windows build script and use official/pinned dependencies. Do not silently download random helper binaries. Preserve logs.

## Test matrix — uninstall

Use disposable, standard non-admin Windows 11 x64 account with synthetic files under a test-owned Windows profile. Never simulate destructive cleanup in the real owner's profile or on real game recordings.

1. Install with Flick-Setup.exe, verify default app binaries under LocalAppData/Programs/Flick, Start Menu Flick + Uninstall Flick shortcuts, optional desktop shortcut and Settings > Installed Apps entry. Verify no unexpected elevation, automatic capture or process injection.
2. Test Settings > General > Uninstall Flick. Click No at the first confirmation, verify nothing is removed and active video recording is not interrupted. Click Yes; verify the authentic Inno uninstaller starts and Flick exits/tray/owned native workers quit.
3. In the uninstaller choose No to the separate local data cleanup question. Confirm program binary, shortcuts, Windows uninstall registration and own Windows startup Run entry are gone, but SHA-256 of settings.json, clips_index.json, connected_sources.json, thumbnail and cache files, original MKVs, MP4 exports and imported media remains unchanged.
4. Reinstall. Choose Yes at both confirmations. Confirm only allowlisted Flick app profile JSON and thumbnail/cache directories are removed, while user-created files in the legacy LocalClip profile are NOT deleted. Verify all original recordings, exports and linked external videos are byte-identical. Do not recursively delete user folder roots.
5. With Windows startup enabled, uninstall from Start Menu and Installed Apps. Confirm only the Run entry targeting this exact installed Flick.exe is removed. Verify unrelated user's registry values, startup programs and custom folders untouched.
6. Simulate /SILENT uninstall in disposable account. It must remove binaries but keep profile and recordings by default. Check installation with spaces/non-ASCII path and safe extraction in portable ZIP. Portable Settings > General > Uninstall Flick must explain manual deletion, never launch an arbitrary EXE or erase installed Windows data.
7. Verify cancel mid-uninstall and existing-app upgrade logic, no orphaned libobs host/FFmpeg processes, no installer data loss or unusual antivirus flags. Check the browser-downloaded Setup EXE remains in Downloads, while the actual installed app is in the selected per-user program directory.

## Hardware, compatibility and release accuracy

- Run the NVIDIA RTX 5060 capture/export pipeline (including 30-second replay and Discord-sized MP4) on qualified Windows x64. Record 127 full test outcomes. Do not infer AMD/Intel GPU support from WPF app launch.
- The source/installer must clearly state Windows 11 x64 with NVIDIA NVENC support; AMD-only, Intel-only, ARM, 32-bit Windows and macOS/Linux unsupported, Windows 10 unqualified. Do not advertise universal GPU compatibility.
- Real 100/125/150/200% display scaling, high contrast, UI layout and normal Uninstall button visibility in General category; actual 2-hour/8-hour soak and FPS/1% lows if authorized, else BLOCKED.
- Verify GitHub tag/Setup binary/ZIP/checksums all correspond to exact release commit and correct v1.0.2-preview.2 metadata; no source leak, personal clips, private screenshots or tokens.

## Final output

A single self-contained PASS/FAIL/BLOCKED table with all installer, uninstall, cleanup, MSI/registry, startup, portable, performance, compatibility and user-data safety tests; include exact SHA, command results, timestamps, binary checksums and reproduction steps for failures. Report GO/NO-GO for **Windows NVIDIA PREVIEW** only, not all GPU Windows or production public release.

Never commit changes; coding owner will fix regressions on GitHub. Preserve the user's personal media and secrets.
