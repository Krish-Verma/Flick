# Flick Windows Downloads and GitHub Release Management

## The simple user experience

**Preferred download:** [Flick-Setup.exe](https://github.com/Krish-Verma/Flick/releases/download/v1.0.1-preview.1/Flick-Setup.exe) from [Flick v1.0.1 Preview](https://github.com/Krish-Verma/Flick/releases/tag/v1.0.1-preview.1). A single EXE runs a standard Windows per-user setup wizard and installs Flick.exe with its runtime dependencies. The Start Menu entry, optional desktop icon and Installed Apps uninstall entry work like those for ordinary Windows software.

**Portable alternative:** Flick-v1.0.1-win-x64.zip. It needs extraction of all files because the app expects bundled runtime directories. Most users should choose Setup.exe.

Users do not need to find an Actions run, download an Actions artifact wrapper ZIP, open an inspection package, install .NET, build sources, or run a PowerShell installer script.

## How GitHub Releases are organized

- **v1.0.0:** Original historical ZIP-only build (no normal user installer). It can remain the Latest **stable** GitHub label.
- **v1.0.1-preview.1:** Newer **private prerelease** for beta testers. The README points directly here, instead of relying on GitHub's stable/latest badge.
- Future public stable releases should be signed, reviewed, and published only after complete physical hardware QA and third-party legal checks.

The repo is currently PRIVATE. Creating a private prerelease does not make code or artifacts publicly accessible.

## Reproducible and controlled publishing

1. Release owner reviews the new preview version and the entire source stack.
2. Merge the approved integration PR to the DEFAULT main branch. The changed **release/preview-version.txt** is an explicit release trigger.
3. The trusted workflow at .github/workflows/publish-preview-release.yml runs only on a main-branch change to that version manifest (or deliberate manual execution on main).
4. Build job downloads pinned OBS and FFmpeg archives and SHA-256-verifies them; compiles the native capture host; runs static GUI contracts, .NET Release build and offline tests; builds both Windows artifacts with Inno Setup.
5. scripts/verify_flick_release.ps1 recalculates actual SHA-256 for Flick-Setup.exe, the portable ZIP, Flick.exe and native CaptureHost.exe, and checks the corresponding runtime/license files.
6. Release job first checks the GitHub repository is PRIVATE and rejects an existing tag; it creates a named **prerelease** and attaches the EXE, ZIP and checksums. The automatically generated source archives point to the same main commit used for the build.
7. Antigravity may test installer, GPU, frame-time, FPS, DPI, hotkeys and data retention. It must not edit code; source corrections belong to the development owner.

Only the release job has scoped contents-write GitHub token permission. The workflow is not triggered by unreviewed PR head commits, feature-branch pushes, or pushes to a public repository. Never use the GITHUB_TOKEN to create a release whose target code differs from the checked-out main commit.

## How to publish future previews

Bump **all** matching version declarations together: release/preview-version.txt, app .csproj Version, dependencies.lock.json, Inno Setup MyAppVersion/AppVerName, portable ZIP directory, release workflow version, source-release static checks, docs, and release notes. Keep the downloadable installer asset name constant: **Flick-Setup.exe**.

Merge that change into main only after tests and review. A newly bumped preview-version.txt initiates a new prerelease and keeps old tagged builds immutable. Do not replace prior assets silently.

## Public open-source launch blockers

This workflow intentionally creates **private prereleases**, not public stable releases. Before making Flick publicly downloadable, confirm full corresponding source and license obligations for the exact OBS/FFmpeg runtimes, artwork ownership, signature/publisher strategy, performance and display QA, multi-hour endurance where required, user privacy and safe uninstall behavior. Obtain explicit owner approval to change repository visibility and to declare a release stable.

An unsigned EXE can be a valid preview, but do not instruct unknown users to bypass real Windows security checks without verifying provenance.

## App installation paths

- Program files: %LOCALAPPDATA%/Programs/Flick
- Existing settings/index/cache: %LOCALAPPDATA%/LocalClip (preserved for compatibility)
- Recordings/exports: user-selected video locations (preserved)
- Historic LocalClip uninstall.ps1 scripts are NOT safe for migrations; never execute them on real user data.

## GitHub Actions artifact layout and release retries

The Actions artifact stores paths under its common root, so downloaded files may be nested as release-files/dist/Flick-Setup.exe or release-files/release/PREVIEW-RELEASE-NOTES.md. The publishing step must **locate each uniquely named file, copy it to a flat release-ready directory, recalculate downloaded SHA-256 values and only then create the prerelease**. A missing/duplicate asset blocks publication without retagging.

The first attempt of v1.0.1-preview.1 compiled its installer and verified the source, but its publish job expected flat artifact paths and therefore stopped before creating a release. This source-only packaging correction also changes the reviewed release-version marker's final whitespace to explicitly retrigger the restricted main-branch release workflow without changing the application version or functional behavior. Retain the failed run as historical QA evidence.
