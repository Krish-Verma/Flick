# Security Policy — Flick

Flick handles sensitive local game footage, recordings, optional microphone audio, filesystem paths and (if enabled) protected Discord webhook secrets. Privacy regressions should be treated as high priority.

## Supported versions

Only the owner's current pre-release draft branch is under active development testing. There is **no publicly supported release or security response SLA** yet.

## Reporting security concerns

If GitHub Private Vulnerability Reporting becomes available after public release, use it. Otherwise, open a minimal issue requesting a private reporting channel without disclosing exploit steps, secret tokens, sensitive media or personal paths in public. Never post working webhook URLs or user recordings in issues or CI logs.

Include affected commit/version, OS, expected versus actual behavior and a safe reproduction using synthetic data when possible.

## Non-negotiable protections

- Replay starts disarmed; microphone off by default; capture source cannot silently change from a missing window to a monitor.
- No accounts, telemetry or background uploads. Discord webhook is optional, disabled by default and requires direct user confirmation.
- Imported source recordings must not be modified/deleted by Remove; exported copies have their own separate directory.
- Uninstaller never deletes legacy user profile, video originals, exports, connection metadata or thumbnail cache.
- Check all media and file paths before processing; constrain subprocess lifetimes with owned Windows Job Objects; never launch unpinned binary downloads implicitly.

Do not make the repository public until a source, dependency and private artifact audit passes.
