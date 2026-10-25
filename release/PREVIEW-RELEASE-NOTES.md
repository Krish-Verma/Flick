# Flick 1.0.8 Preview 1 — Replay Save & Audio Device Reliability (UNRELEASED · UNSIGNED)

This is a **source/installer release candidate**, not yet a public download. Public GitHub Latest remains v1.0.7-preview.1 until a separately approved, verified release-publishing operation. The owner reported that a single intended Save Clip shortcut press could fail with “NotReady” while replay unexpectedly entered Stopping; the recording engine also produced valid files that did not immediately trigger a success notification. The prior diagnostics did **not** prove what initiated that stop, so this update adds a protective guard and structured event provenance rather than claiming a definitive reproduction.

## Changes from 1.0.7

- **Protect pending clip saves.** An incidental global Replay Toggle hotkey cannot stop capture while a native replay save or its related acceptance work is pending; deliberate UI/tray Stop remains supported. A duplicate save action while a native save is outstanding reports “still saving” rather than a second false failure banner.
- **Truthful save acknowledgments.** An eight-second IPC acknowledgment timeout cannot immediately be treated as a definitive failed clip while the native helper still tracks the request. The final corresponding native success, failure, or confirmed host exit determines its outcome.
- **Faster success notification.** A validated media file that is durably indexed triggers “Clip Saved” promptly; optional thumbnail generation is deferred to existing lazy-thumbnail work rather than adding up to 15 seconds to the normal save path.
- **Private action diagnostics.** A closed, allowlisted origin/action category identifies replay start/stop/save from global hotkey, capture bar, tray, Settings or Quit. No raw keys, source titles, process identifiers or paths are added to the exported diagnostic journal.
- **Audio device selectors.** Settings → Audio now lists Windows playback/output endpoints and microphone/input endpoints, including a “Windows default” option, named active devices and an explicit unavailable-device warning. Audio device discovery is read-only and off the UI thread; it does not change system playback settings or activate the microphone. Explicit microphone recording remains opt-in, with settings staged until Save and confirmation required for changes during active replay.

The existing separate Game/Voice process-audio choices and v1.0.7 gameplay, library, editing and sharing functionality are preserved. This release does not alter the OBS encoder selection policy, network behavior, recording consent, startup policies or saved file format.

## Installation and user data — after approval

Once officially published, install the newly versioned **Flick-Setup.exe** over an existing per-user installation without uninstalling the older copy first. Portable builds use **Flick-v1.0.8-win-x64.zip** and require replacing the extracted program folder only after quitting Flick. Original clips, metadata, audio preferences, library index, imported sources and app settings stay in their existing user-data directories, separate from program binaries. Versions prior to supported updater builds may need a one-time manual installation.

Do not delete or move existing recordings or change unrelated Windows security/driver/audio-device settings to install or test. The default microphone stays disabled. Replay is initially Off unless a user has explicitly enabled eligible approved-game automatic recording.

## Qualification and security

**UNSIGNED:** The candidate has no trusted Windows publisher signature and may be blocked by Smart App Control. Checksums establish artifact integrity, not publisher trust. Do not ask users to disable Windows security protections. Microsoft Store/MSIX signing and direct signing are independent future work.

**REAL-HARDWARE ACCEPTANCE PENDING:** CI validates managed and native compilation, deterministic replay state/diagnostic tests, generated-media workflows, UI rendering, packaging and installer upgrade behavior. Only the owner can qualify the single-press shortcut behavior in their game, physical audio endpoint capture (including selected headset/microphone), exclusive-fullscreen notification, capture FPS impact and extended reliability on an authorized Windows gaming PC. The earlier successful Recording Test and synthetic test cases are not substitutes.

The build targets Windows 11 x64 and requires compatible Direct3D11 graphics drivers and Microsoft Visual C++ v14 x64 redistributables. Keep bundled OBS and FFmpeg files intact. Original Flick source remains GPL-2.0-or-later and dependencies retain their respective license/notice obligations.

**This document does not authorize publication or a GitHub Latest change.** Pin the final exact source tree, successful same-source Windows CI/WPF/installer workflows, artifact/archive and Setup hashes, plus independent public download verification, before any separately approved release.
