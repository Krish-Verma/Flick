# Security Policy — Flick

Flick handles sensitive local game footage, recordings, optional microphone audio, filesystem paths and (if enabled) protected Discord webhook secrets. Privacy regressions should be treated as high priority.

## Supported versions

Flick is a public repository with preview downloads and active development. The published 1.0.4-preview.1 design preview predates the 1.0.5-preview.1 compatibility candidate. There is no published security response SLA or long-term support policy yet. Report the exact version and source commit involved.

## Reporting security concerns

Use GitHub's private vulnerability reporting option in this repository's Security tab if enabled; this document does not assert that the setting is enabled. Otherwise, open a minimal issue requesting a private reporting channel without disclosing exploit steps, secret tokens, sensitive media or personal paths in public. Never post webhook URLs or recordings in issues or CI logs.

Include affected commit/version, OS, expected versus actual behavior and a safe reproduction using synthetic data when possible.

## Non-negotiable protections

- Replay starts disarmed; microphone off by default; capture source cannot silently change from a missing window to a monitor.
- No Flick account, analytics client or automatic media upload. Requested update checks/downloads contact GitHub; optional Discord posting is disabled by default and requires confirmation. See [Privacy](PRIVACY.md).
- Imported source recordings must not be modified/deleted by Remove; exported copies have their own separate directory.
- Silent/default uninstall retains user data. Explicit profile-removal consent deletes only allowlisted settings/index/cache files; originals, exports, imported sources and unknown files remain outside that deletion allowlist.
- Check all media and file paths before processing; constrain subprocess lifetimes with owned Windows Job Objects; never launch unpinned binary downloads implicitly.

Keep source, dependencies and public artifacts free of credentials and personal media. Public visibility does not establish that every historical/current artifact has completed all release audits.


## Windows Smart App Control and published binaries

The published v1.0.4-preview.1 installer is **unsigned**. A checksum is not a trusted publisher signature; Smart App Control may block it even when CI passes. Approval of a labelled unsigned preview does not establish trusted publisher status. Do not encourage disabling Smart App Control, adding Defender exclusions or changing Code Integrity policy to run it.

Sign only approved Flick-owned artifacts with a real trusted identity. Preserve upstream executables and verified hashes; do not stamp unmodified upstream code with Flick's proposed Foundation certificate. Strict all-executable trust and physical SAC acceptance remain separate requirements for a trusted release. Foundation approval and an operational Foundation signing workflow have not been verified. See [Code signing policy](CODE_SIGNING_POLICY.md), [SignPath setup](docs/SIGNPATH-RELEASE-SETUP.md), and [Windows trust](docs/WINDOWS-TRUST-AND-SMART-APP-CONTROL.md).
