# Approved Flick 1.0.6 unsigned replay-fix preview publication

User authorization: publish the existing PR #33 fixes as a separate versioned Flick preview and mark the successfully verified GitHub release **Latest**. Do not overwrite any existing download, delete historical tags/assets, claim publisher trust, or declare all Windows GPUs hardware-qualified.

## Immutable source and verification receipt

| Evidence | Pinned value |
| --- | --- |
| Approved 1.0.6 source commit | `d63043519eb7348d32efd129835cad9c2bb26fe7` |
| Approved source tree | `74f054ae3a3a69b51e8dd147ccd3cead8badce5e` |
| Main merge retaining the source | `a4f23e4226a6687fe731cab4ddfa46ecf9d3aaf0` |
| Windows CI (completed successfully) | `40084437463` |
| WPF rendering (completed successfully) | `40084437461` |
| Installer/package lifecycle (completed successfully) | `40084437470` |
| Artifact source merge commit | `0a6fa3f6e366f53c39504ae35be9e3dc05e13a65` |
| Verified artifact ID | `12556043662` |
| Complete artifact archive SHA-256 | `sha256:b78a6cb7b1aec691034d13f1f10653e22acbbe1934ff4efb8dd9efd681df6c25` |
| Preview installer SHA-256 | `884B5B784D3DA046696C1097D36CE7BEE6917CD40FCA0153A2333511BBC6B4A5` |
| Portable ZIP SHA-256 (CI log) | `61A14F70F8361D6F30C82A7E3A54DDCCD150020DB0704D7D073A835DE5F7B0E9` |

The actual merged-source tree and the PR artifact's synthetic merge tree were independently verified identical to the approved source tree. The package job verified portable and setup hashes, native OBS/FFmpeg contents, all four Flick-owned EXE/DLL versions and install/update/restart/user-data lifecycle. It is **not** a physical GPU clipping benchmark.

## One-time publisher workflow

The prior trusted-by-evidence publication pattern remains in `.github/workflows/publish-approved-compatibility-preview.yml`. The new PR changes this existing registered workflow, with same-repository branch `release/publish-replay-preview-20261024` and `main` base guards; only the publisher job receives `contents:write`. It checks exact source identity, all three same-source green workflows, immutable artifact ownership/build tree/archive hash, all three unpacked files and manifest hashes before release mutation.

The publisher first creates the new `v1.0.6-preview.1` Release in **draft** state, with exact `Flick-Setup.exe`, `Flick-v1.0.6-win-x64.zip`, `SHA256SUMS.txt` and explicit **UNSIGNED REPLAY FIX PREVIEW** title/notes. After verified new asset IDs, sizes and digests it updates the release to public Latest (GitHub requires `prerelease=false`; semantic `preview.1` and unsigned disclosure remain explicit). It then independently downloads all three public files without user credentials and verifies exact hash/size. Only **after this** and an exact three-release catalogue check does it hide the prior public 1.0.5 as a **draft**.

The older 1.0.4 draft remains unchanged. The existing 1.0.5 tag, release entry and all its assets are kept for rollback/inspection; no destructive release or tag operation is permitted. The prior draft 1.0.4 is still used as the authenticated installer-upgrade test fixture.

The workflow writes a publication receipt even if a gate fails and aborts on any unexpected tag, archive, asset or release catalogue. Do **not** blindly rerun a partial publisher if a new draft/public release already exists; inspect its receipt, release state, and asset hashes first.

## Release claims and limitations

The 1.0.6 preview includes:
- Save controls enabled only in Replay Ready state, not Buffering.
- Failure notice and actionable status when pressing Save with replay disabled/unready, while preserving native detailed faults.
- Actual Windows hotkey-registration conflict reason rather than a generic message.
- Failed clip rename caught without app UI crash.
- Delete confirmation discloses the associated exported MP4 may also be permanently removed.
- New WPF regression coverage on readiness and failure feedback.

Replay stays **OFF at every launch** and must be started intentionally. No native OBS encoder changes from 1.0.5, no new data collection, no change to GPL-2.0-or-later and third-party license obligations.

**UNSIGNED:** Smart App Control may block this download. Checksums validate downloaded bytes but do not establish publisher identity. SignPath approval is not assumed. Microsoft Artifact Signing and Store MSIX remain future independent signing options.

**Hardware still requires AntiGravity acceptance:** Windows gameplay/NVIDIA/AMD/Intel/hybrid/CPU, physical shortcuts/Fn keys, clip saves and valid decoding, audio, FPS impact, repeated save/endurance, and enforced Smart App Control tests. Release Latest does not mean signed/stable/all-hardware-qualified.

Publication may be considered complete only after the publisher run finishes successfully and independent GitHub API confirms `GET /repos/Krish-Verma/Flick/releases/latest` resolves to `v1.0.6-preview.1`.
