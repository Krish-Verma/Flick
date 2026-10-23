# Flick — lightweight gameplay clipping for Windows

**Latest download: [Flick 1.0.4 — UNSIGNED Design Preview](https://github.com/Krish-Verma/Flick/releases/latest).** See [software updates](docs/SOFTWARE-UPDATES.md) for the one-time upgrade from older builds.


Flick is a Windows-native game clipper for capturing the last 15–120 seconds, trimming a clip without altering its original file, and exporting an MP4 suitable for sharing. The product is designed to be free, offline-first and open source, without accounts, telemetry, forced uploads or a heavyweight permanent gaming overlay.

**Status: unsigned developer preview, not a trusted public release.** The repository is public; Windows hardware acceptance and trusted signing remain separate release gates. The application is distributed under GPL-2.0-or-later for original Flick code, subject to third-party component terms. Unsigned preview builds are explicitly labelled; they are not stable, Windows-trusted releases.

## Features

- Explicit manual replay on/off, separate global hotkeys for Save Clip and Toggle Replay; recording starts **off** even at Windows login.
- NVIDIA NVENC video replay via a separate libobs capture worker, with 15–120 second duration and bounded buffer.
- Native WPF clip library with search, in-place import of other recording folders, offline-folder awareness and non-destructive editing.
- Two-handle trim, per-track audio mute/gain, local original/export preview; user-controlled export folder.
- Size-capped H.264/AAC MP4 export for Discord (20 MB default, 10/50/100 MB and custom sizes), FileDrop drag and Copy File. Optional webhook posting is disabled by default and requires explicit consent.
- Compact Flick-branded two-second Clip Saved notification and optional chime; each independently toggleable.
- Native Windows application, optically aligned Flick logo and purple interface, refined toolbar icons and subtle logo gradients.
- In-app update checks, verified downloads and confirmed install/restart for installed copies.

## What the current Windows build supports

**Supported/qualified:** Windows 11 on x86-64 with a compatible NVIDIA GPU and NVENC recording. Tested on an RTX 5060. **Not yet supported for replay recording:** AMD-only graphics, Intel-only graphics, Windows-on-ARM, 32-bit Windows, macOS and Linux. Windows 10 has not been qualified. These are future expansion targets, not capabilities of this release.

**Uninstall:** Flick Settings > General > Uninstall Flick opens the Windows installer’s removal tool. The Start Menu and Windows Installed Apps also offer Uninstall. After explicit confirmation, you can remove local settings, clip indexes and cache; saved videos/exports/imported originals remain safe. The setup file downloaded in Downloads is separate from the installed program and may be manually deleted after installation. Neither the Windows installer nor the uninstaller can guarantee the removal of Windows-managed history/logs.

## Windows requirements

Windows 11 x86-64 with a supported NVIDIA NVENC GPU is the qualified target. AMD/Intel-only recording, Windows ARM and 32-bit Windows are unsupported; Windows 10 has not been fully validated. Bundled OBS and FFmpeg libraries are required at runtime. Self-contained .NET 10 publishing removes the need for end users to install the .NET SDK.

## Download Flick for Windows

**[Flick v1.0.4 Preview 1 — UNSIGNED](https://github.com/Krish-Verma/Flick/releases/tag/v1.0.4-preview.1)**

**[Download Flick-Setup.exe](https://github.com/Krish-Verma/Flick/releases/download/v1.0.4-preview.1/Flick-Setup.exe)**

Run Setup to install Flick for your Windows account, with a Start Menu shortcut and optional desktop shortcut. The package includes .NET, OBS and FFmpeg. Windows may block this unsigned preview; it has no verified publisher identity. Check SHA-256 against the release's SHA256SUMS.txt. Trusted signing and physical Windows/NVIDIA acceptance remain pending.

Older installed versions need **one manual upgrade** to this release. Install over the existing Flick installation without uninstalling it. Thereafter use **Settings → General → Software Updates** to check, download and install a newer release, with explicit confirmation before Flick restarts. An update waits for recording, clip saving, imports, exports, scans and uploads to finish. See [Software Updates](docs/SOFTWARE-UPDATES.md).

## Run or install

1. **Flick-Setup.exe** installs under **%LOCALAPPDATA%\Programs\Flick**, registers Flick in Windows Installed Apps and needs no administrator access.
2. **[Flick-v1.0.4-win-x64.zip](https://github.com/Krish-Verma/Flick/releases/download/v1.0.4-preview.1/Flick-v1.0.4-win-x64.zip)** runs as a portable copy. Extract the entire folder before launching Flick.exe; keep the packaged runtime files together. Portable copies link to Releases for manual folder replacement after quitting Flick.

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
