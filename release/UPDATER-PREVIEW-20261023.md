# Flick 1.0.3 Preview 1 — UI Refresh, Software Updates and Brand Alignment

**UNSIGNED public prerelease.** Windows has not verified the publisher of this installer. Smart App Control may block it. Flick does not disable or bypass Windows protections. This is a preview for testing, with physical Windows/NVIDIA acceptance and trusted signing still pending.

## Install or upgrade

Download **Flick-Setup.exe** and compare its SHA-256 with **SHA256SUMS.txt**. Run Setup over your existing per-user Flick installation; you do not need to uninstall first. Saved settings, library data and recordings live separately from the installed program.

Older versions need this one manual upgrade to gain the updater. Afterward, use **Settings → General → Software Updates** to check for a newer version, download it and confirm installation/restart. Preview releases are an explicit channel choice. Stable unsigned installers are rejected; unsigned previews require a clear warning and consent. The updater checks release origin, version, byte size and SHA-256 before launch.

Portable users can download **Flick-v1.0.3-win-x64.zip**, quit Flick and replace the extracted application folder. Extract the entire package, including its runtime files. Portable copies do not convert themselves into installed copies.

## Changes

- Completed the R3–R8 workspace: a clear library/player layout, widely spaced editing actions, focused Trim/Audio/Share workspaces and grouped settings.
- Clip notifications show only **Clip Saved** and the duration, with no clip title, filename, path or ID.
- Preserved the restrained graphite/lavender theme and original sunset artwork; corrected optical alignment in WPF, tray and all packaged icon sizes.
- Added cancellable update checks/downloads and confirmed installer handoff. Updates wait for capture, clip saving/indexing, imports, exports, library scans and uploads to finish. Setup waits for the old process to exit, updates the same installation and restarts Flick.
- No idle update polling, account requirement or embedded GitHub credential. Updates download the full installer; delta updates are not implemented.

## Validation and limits

Windows build/tests, native capture-host compilation, actual WPF control renders and disposable installer lifecycle checks are linked in the publication evidence below. The lifecycle check covers install/reinstall, exact-parent waiting, restart, unchanged AppId, file hashes and retained user data; it uses a surrogate parent holding Flick's mutex. It does not replace a physical upgrade from every older version.

Real NVIDIA recording/audio, gameplay, physical DPI transitions, Smart App Control acceptance and endurance qualification remain separate. The app supports the existing Windows x64/NVIDIA preview target; this release does not add AMD, Intel-only or ARM recording support.

Original Flick source remains GPL-2.0-or-later; bundled third-party components retain their own terms. See LICENSE-NOTICE.md and THIRD-PARTY-NOTICES.md in the source/package.


## Publication evidence

- Approved application source: `7123509de13dc302a632bda5ac3cc986a38d9d22`.
- Built/rendered merge: `f85f39f64d40cefcf2b7d4888745ace54886757c`, with the identical source tree `1a4f1c62c844374580ce06b08525256d2683d052`.
- [Windows CI](https://github.com/Krish-Verma/Flick/actions/runs/39982356180): 215 tests passed, 0 failed, 7 hardware tests skipped; WPF and native builds passed.
- [Actual WPF and native-icon renders](https://github.com/Krish-Verma/Flick/actions/runs/39982356340): passed and visually inspected with synthetic fixtures.
- [Setup/package and installed-update lifecycle](https://github.com/Krish-Verma/Flick/actions/runs/39982356386): passed, including uninstall cleanup and preserved unknown user data. This does not qualify physical GPU/SAC behavior.
- Immutable package artifact: `12513783914`, digest `sha256:5d0b0b83177060565a592528f87056a1385123403056d858d62990a72c1b86b6`.
- Setup SHA-256: `B19E97E454B9A9636CF1F12F60599B1B2D7949AF110796F615A3D0F55673BD38`.
- Portable ZIP SHA-256: `5AED5B95509B0BEBA75A55E546D271F52D2610FD067E8AB30545ED8E86A87644`.
