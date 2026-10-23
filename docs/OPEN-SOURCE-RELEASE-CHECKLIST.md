# Flick — Open-source Release Checklist (owner sign-off required)

**October 23 status:** The owner made the repository public and authorized clearly labelled unsigned previews. The approved v1.0.4 design preview is the single Latest download, with older Release entries removed after verification. GitHub's catalogue flag is separate from the semantic preview version and unsigned-install consent. Windows CI and installer lifecycle evidence do not qualify trusted signing, physical Windows/NVIDIA acceptance, legal readiness, or a stable release. See [Software Updates](SOFTWARE-UPDATES.md) for current delivery behavior. The checklist below retains the earlier QA baseline and pending acceptance criteria.

**Historical baseline (before October 23 owner approval):** private GitHub development repository, stacked draft GUI PR #7 and release-foundation draft PR #8. No public version tag or release is authorized. Pass/fail below must be filled with exact commit hashes and durable raw evidence by Windows QA.

| Gate | Status before AntiGravity | Objective acceptance |
|---|---|---|
| Native .NET/GUI and capture-host CI | Source CI eligible | 0 build warnings/errors, 120+ offline tests pass, native host build |
| Real GPU integration | Prior PR #7 reported passing | Rerun hardware tests on PR #8 exact HEAD, 0 skipped |
| Idle RAM, lazy preview and thumbnail effects | **UNMEASURED** | Baseline vs changed build, repeatable WS/private/handle and FPS traces |
| Typical Windows Setup.exe | **SOURCE IMPLEMENTED, EXE NOT HARDWARE APPROVED** | Build with pinned tools, test clean install/upgrade/uninstall, standard non-admin account |
| Uninstall preserves user clips/settings/index | **SOURCE GUARDED, NOT HARDWARE VERIFIED** | Seeded data SHA-256 and byte-identical after uninstall |
| Legacy LocalClip installations | **NOT QUALIFIED** | Back up legacy profile and test migration without old destructive uninstaller |
| Windows system integration | **UNMEASURED** | Flick.exe icon, Start Menu, desktop opt-in, Apps entry, launch/tray, no auto capture |
| Windows display and gameplay | **UNMEASURED FOR THIS BRANCH** | DPI 100/125/150/200%, focus, high-contrast, fullscreen, real gameplay |
| Endurance | **BLOCKED** | 2-hour personal and 8-hour release soak if owner authorizes |
| Code signing / Windows reputation | **NO CERTIFICATE** | Owner decides whether/how to sign, test authenticity and warnings |
| Open-source legal readiness | **REQUIRES REVIEW** | Confirm asset rights, GPL-2.0-or-later notices, OBS/FFmpeg build flags, licenses and corresponding source obligations |
| Security and privacy | **REQUIRES REVIEW** | No accounts/telemetry; local-only originals, mic opt-in, webhook consent/DPAPI, no accidental media upload |
| Public distribution | **NOT AUTHORIZED** | Owner explicit approval; then tag, source archive, checksums, changelog, signed/provenanced artifacts when available |
| Repo visibility | **PRIVATE BY DESIGN** | Owner explicitly approves switching to public after removing private QA media and secrets |

## Distribution legal checklist

- Confirm Flick original code ownership and third-party asset permissions, including all logos and screenshots. User-provided artwork is not independently licensed merely because it appears in source.
- The repository includes full GPLv2 license text and an explicit **GPL-2.0-or-later** notice for original Flick code. Third-party parts have their own notices.
- Record exact OBS and FFmpeg binary builds and SHA-256 and inspect their actual build configurations. Link/copy complete corresponding source and all required license notices or otherwise satisfy the licenses for the exact shipped binary. Do not claim compliance just because upstream source URLs are listed.
- Review build/release reproducibility, dependency pinning and update security; do not replace pinned binaries with unverified downloads.
- Keep release artifacts and CI logs free of personal games, videos, webhook URLs and connection secrets; synthetic fixtures only.
- Consider Windows code signing before public distribution. Unsigned Setup.exe is not the same as a malicious program, but users should not be instructed to disregard legitimate OS warnings without verifying provenance.

## Owner release steps (only AFTER evidence approval)

1. Complete the [test-only AntiGravity run](FLICK-PREVIEW-ANTIGRAVITY-TEST-HANDOFF.md); resolve defects through the development owner and rerun affected tests.
2. Confirm ancestor PRs, current protected branch, full review, license attribution, source archive and reproducible checksum manifest.
3. Merge only with owner permission; build from exact frozen commit; preserve public source and accompanying install artifact checksums.
4. Approve making GitHub repository public and publishing a release separately. Avoid quietly uploading unsigned or unverified packages under a final-looking version.
5. Provide a clean README installation guide, known limitations and support path for community users.

## v1.0.2 installer and compatibility gates

- Verify in-app Settings > General > Uninstall Flick prompts before dispatching the real registered Windows uninstaller. Portable mode must not self-delete.
- On the Windows non-admin test account, use installed-app uninstall, Start Menu uninstall and in-app uninstall. Verify safe exit of active libobs replay worker, tray and global hotkeys, and no unintended leftover program files/shortcuts/Run registration.
- Opt-out data removal: existing JSON, thumbnails, videos and exports retain byte-identical SHA-256 hashes. Opt-in data removal: only allowlisted app settings/index/caches disappear, while original clips, exports and imported sources remain byte-identical. Silent uninstall must retain user data by default.
- Check the downloaded Setup EXE remains a separate Downloads file while the actual installed app is located under the selected program directory. Uninstall should not silently erase Downloads, Windows Defender logs, history or arbitrary user files.
- Verify native x64 Windows NVIDIA NVENC hardware. Explicitly mark AMD-only/Intel-only/ARM/Windows 10 compatibility UNSUPPORTED or UNQUALIFIED rather than PASS.
- Third-party licenses/source, code signing and public visibility review remain distinct from installer mechanics. An unsigned PREVIEW on a private repository is not a public stable release.


## Smart App Control — public signing gate (2026-10-22)

- Existing v1.0.2-preview.2 unsigned installer is blocked by Windows Smart App Control and must not be labeled trusted or public stable. No per-app bypass.
- Obtain a Public Trust code signing identity through a verified CA (recommended Azure Artifact Signing) or approved SignPath Foundation OSS program.
- Sign and timestamp all shipped code and Setup EXE and generated Inno Uninstaller. Verify every Windows EXE/DLL Authenticode Status Valid and matching first-party signing identity.
- Test Smart App Control ON/enforced on real Windows 11 NVIDIA PC, log CodeIntegrity events and all feature paths. Ensure no remaining unsigned helper.
- Require third-party OBS/FFmpeg license/corresponding-source audit, historical repo secrets/media audit, publisher certificate verification and explicit owner public-stable approval.
- One-time release list cleanup preserves Git tags while deleting superseded GitHub Release entries. Removing release links does not grant Smart App Control trust.
