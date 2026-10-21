# Flick — lightweight, private gameplay clipping for Windows

Flick is a Windows-native game clipper for capturing the last 15–120 seconds, trimming a clip without altering its original file, and exporting an MP4 suitable for sharing. The product is designed to be free, offline-first and open source, without accounts, telemetry, forced uploads or a heavyweight permanent gaming overlay.

**Status: developer preview / not public-release approved.** This repository is currently private while the source and Windows installer pass final acceptance. The application is distributed under GPL-2.0-or-later for original Flick code, subject to third-party component terms. Public repository visibility and releases require explicit owner approval.

## Features

- Explicit manual replay on/off, separate global hotkeys for Save Clip and Toggle Replay; recording starts **off** even at Windows login.
- NVIDIA NVENC video replay via a separate libobs capture worker, with 15–120 second duration and bounded buffer.
- Native WPF clip library with search, in-place import of other recording folders, offline-folder awareness and non-destructive editing.
- Two-handle trim, per-track audio mute/gain, local original/export preview; user-controlled export folder.
- Size-capped H.264/AAC MP4 export for Discord (20 MB default, 10/50/100 MB and custom sizes), FileDrop drag and Copy File. Optional webhook posting is disabled by default and requires explicit consent.
- Compact Flick-branded two-second Clip Saved notification and optional chime; each independently toggleable.
- Native Windows application, full Flick squircle logo, dark graphite UI; no Electron or cloud account.

## Windows requirements

Windows 11 64-bit is the primary target; NVIDIA NVENC GPU required for the initial capture pipeline. Some devices/codecs, fullscreen modes and non-NVIDIA systems are not qualified. Bundled OBS and FFmpeg libraries are required at runtime. Self-contained .NET 10 publishing removes the need for end users to install the .NET SDK.

## Run or install

The source builds two **different** local preview deliverables:

1. **Flick-v1.0.0-win-x64.zip** — extract everything into one folder and launch **Flick.exe**. Do not move only the EXE: it requires the packaged OBS/FFmpeg runtime directory.
2. **Flick-Setup-1.0.0-win-x64-preview.exe** — a conventional per-user Inno Setup wizard. It installs binaries under **%LOCALAPPDATA%\Programs\Flick**, registers Flick in Windows Installed Apps, creates a Start Menu shortcut and optionally a desktop shortcut. It does not require elevation.

Neither artifact is published as a public release. The setup build is currently **unsigned**, and Windows may show a publisher/SmartScreen warning. Do not bypass warnings for untrusted downloads. The actual installer requires a clean-machine AntiGravity smoke test before distribution.

Flick saves settings, indexes and thumbnail cache under the **legacy %LOCALAPPDATA%\LocalClip** path for compatibility with previous builds. Default original videos are in your Videos/LocalClip/Originals folder (or a user-selected folder). The new program directory is **different**: an ordinary uninstall removes program files, not your recordings or legacy profile data. **Do not run older LocalClip uninstall.ps1 scripts:** those previously removed the whole legacy data directory.

See [Installation and Migration](docs/INSTALLATION-DISTRIBUTION.md) for upgrade and safe-uninstall instructions.

## Build from source

On a supported Windows development machine, install the pinned .NET 10 SDK from global.json, MinGW-w64 GCC, and optionally **Inno Setup 6** for the graphical installer. Review and run the pinned download script only from a trusted repository checkout.

~~~powershell
./scripts/setup_tools.ps1
dotnet restore LocalClip.slnx
dotnet build LocalClip.slnx -c Release --warnaserror
./scripts/run_tests.ps1
./scripts/check_flick_release_source.ps1
./scripts/build_package.ps1
./scripts/build_installer.ps1 -SkipPortable
~~~

The last command compiles a local unsigned Setup EXE from the previously built portable package. To build the installer and portable payload in one step, omit -SkipPortable. ISCC.exe from Inno Setup 6 must be installed; scripts never silently download an installer compiler.

The optional real-GPU test command is **./scripts/run_tests.ps1 -Hardware** on an explicitly authorized Windows/NVIDIA rig. Hardware tests and GPU FPS measurements are not replaced by GitHub-hosted CI.

## Code organization

- **src/LocalClip.App** — WPF UI, clip library, editor, icon and tray.
- **src/LocalClip.Core** — media, storage, IPC, privacy and rules.
- **src/LocalClip.CaptureHost** — standalone native C++ libobs worker.
- **installer/Flick.iss** — declarative per-user Windows Setup wizard.
- **scripts/build_package.ps1** and **scripts/build_installer.ps1** — reproducible build steps from pinned inputs and SHA-256 reporting.
- **docs/PERFORMANCE-OPTIMIZATION-IMPLEMENTATION.md** — optimization rationale and real-world benchmarking protocol.
- **docs/OPEN-SOURCE-RELEASE-CHECKLIST.md** — release blockers and artifact provenance.
- **docs/FLICK-PREVIEW-ANTIGRAVITY-TEST-HANDOFF.md** — separate hardware/installer acceptance; AntiGravity tests but does not edit source.

## Privacy and security

Flick has no required login and no built-in telemetry/cloud synchronization. It only records a source after manual arming. Microphone recording is opt-in; webhook publishing is disabled by default and requires confirmation. Review source-selection and permissions before gameplay. A linked import folder is indexed locally without copying or deleting imported originals.

## Contributing and licensing

See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md), [LICENSE](LICENSE), [LICENSE-NOTICE.md](LICENSE-NOTICE.md), and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Before publishing binaries publicly, verify complete corresponding source and actual third-party license obligations for shipped OBS/FFmpeg builds. Unapproved preview artifacts are not a public release.
