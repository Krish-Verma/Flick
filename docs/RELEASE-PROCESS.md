# Flick Windows Downloads and GitHub Release Management

## The simple user experience

**Preferred download:** [Flick-Setup.exe](https://github.com/Krish-Verma/Flick/releases/download/v1.0.2-preview.2/Flick-Setup.exe) from [Flick v1.0.2 — Windows NVIDIA Preview](https://github.com/Krish-Verma/Flick/releases/tag/v1.0.2-preview.2). A single EXE runs a standard Windows per-user setup wizard and installs Flick.exe with its runtime dependencies. The Start Menu entry, optional desktop icon and Installed Apps uninstall entry work like those for ordinary Windows software.

**Portable alternative:** Flick-v1.0.2-win-x64.zip. It needs extraction of all files because the app expects bundled runtime directories. Most users should choose Setup.exe.

Users do not need to find an Actions run, download an Actions artifact wrapper ZIP, open an inspection package, install .NET, build sources, or run a PowerShell installer script.

## How GitHub Releases are organized

- **v1.0.0:** Original historical ZIP-only build (no normal user installer). It can remain the Latest **stable** GitHub label.
- **v1.0.2-preview.2:** Newer **private prerelease** for beta testers. The README points directly here, instead of relying on GitHub's stable/latest badge.
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

Historical incident: the first v1.0.1-preview.1 publishing workflow failed because it expected downloaded GitHub Actions artifacts at flat paths rather than their original nested paths. The release publishing logic was corrected to normalize and hash-check every downloaded asset before publication. Preserve historical failed CI runs as evidence, but do not confuse them with the current v1.0.2-preview.2 release gates.

## Public availability versus release labeling

The owner has requested eventual public distribution, but a GitHub Release created in a PRIVATE repository is still visible only to collaborators. Public accessibility requires an administrator to change repository visibility in GitHub Settings > General > Danger Zone > Change repository visibility, after checking all branches/commits/artifacts for secrets and confirming licenses and third-party redistribution. Changing a private tag from prerelease to stable does NOT make it publicly downloadable.

When the repository is switched to public, previously created non-draft release assets may also become publicly accessible. Keep the v1.0.2 Windows NVIDIA build explicitly labeled PREVIEW while AMD/Intel graphics, ARM, unsigned installer trust, FPS, endurance and licensing checks remain incomplete. Do not assert that it works on all Windows GPU configurations.

A public stable/GA release is a separate user-authorized action only after compatibility and GPL dependency review. Every downloadable binary must correspond exactly to its tagged source and checksum manifest.


## Signed public release prerequisite and old-release cleanup

Windows Smart App Control blocked the UNSIGNED v1.0.2-preview.2 installer. Until a Public Trust signer is provisioned and a signed build passes SAC ON testing, do not publish further unsigned releases or mark preview as stable/public. The legacy unsigned auto-publishing workflow is disabled; signed-windows-qa.yml is a manual private signed candidate only.

The owner requested removing confusing older download choices. A one-time private GitHub Actions workflow archive-old-releases.yml removes GitHub Release entries for v1.0.0, v1.0.1-preview.1 and v1.0.2-preview.1 ONLY after checking the exact current v1.0.2-preview.2 asset digest and source SHA. Git source tags and commits remain, and docs/ARCHIVED-RELEASES.md captures artifact hashes. This does not make the repo public or solve SAC.

The connected GitHub tools cannot change repository visibility. An authorized administrator must use Settings > General > Danger Zone > Change repository visibility after scanning ALL Git history/releases and licensing. Make only a CA-signed, QA-approved new release Latest Stable after public-visibility review.
