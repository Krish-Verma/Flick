# Flick software updates

## Existing installations need one bootstrap upgrade

Flick builds through the October 23 UI-refresh candidate have no updater. Install **v1.0.3-preview.1** once over the existing per-user installation to obtain Settings → General → Software Updates. The existing Inno AppId, install mode and program/data separation remain; uninstalling the older build is not required. Portable builds do not silently convert themselves into installed builds.

## Update flow

The user explicitly checks public `Krish-Verma/Flick` GitHub Releases. No account, embedded GitHub token, telemetry, periodic idle listener or background checking process is required. The updater chooses a newer semantic version with a valid Setup EXE and checksum manifest. Stable installations do not silently opt into preview releases; preview choices are explicit.

Download is cancellable and bounded. Metadata, URL origins, expected byte size and SHA-256 are checked. Partial, truncated or hash-mismatched downloads are discarded. Hashes verify consistency with the repository's release; they do not replace publisher identity or code signing. The final installer is rechecked immediately before launch.

Installation requires explicit consent. Stable unsigned updates are refused. Unsigned preview installation requires a separate, clear warning/choice and may still be blocked by Windows Smart App Control. Flick never disables or bypasses SAC/Defender. A corrupt or untrusted signature must never be downgraded to an ordinary unsigned preview.

Flick refuses installation during replay capture/transitions, a save/indexing operation, import, export, library scan or upload. The user finishes those activities first. On an approved installed-app upgrade, the installer waits up to 30 seconds for the exact old Flick process to exit cleanly; it never kills a recording process. Inno installs using the original per-user AppId and directory, preserves settings/library/media, then restarts Flick. Normal installer launches do not inherit this special update restart path.

## Release workflow

1. Change the semantic app/installer/package version together; never replace bytes under an old release.
2. Pass Windows WPF/native CI, core/update/WPF staging tests and source contracts.
3. Build Setup EXE, portable ZIP and SHA256SUMS.txt; verify actual hashes and pinned source provenance.
4. Publish a distinct GitHub prerelease or stable release with direct assets. The old unsigned auto-publisher remains disabled; this owner-approved preview is a separately audited publication. Trusted signing remains mandatory before describing a build as signed/trusted.
5. Test from the previous installed version, including cancellation, network failure, size/hash/signature rejection, busy capture and install/restart/data retention. Signed public stable releases require actual Windows trust acceptance.

The first updater-enabled release cannot update its own already-installed predecessor automatically: users must bootstrap it once. Later releases can be obtained from within Flick. Updates still download bytes; the convenience is avoiding manual browsing/download/reinstallation steps. Differential/delta updates are not implemented by this initial installer-based updater.
