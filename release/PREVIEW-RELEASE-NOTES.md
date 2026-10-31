# Flick 1.0.13 Preview 1 — Replay Startup Fix (UNSIGNED)

- Fixed normal monitor Replay being rejected by a recording-test capability check before the capture helper or encoder started. Fresh manual and background Replay now verify the selected display on the current helper before recording.
- Diagnostic recordings retain their separate consent and verified-helper checks. Missing or changed displays never silently fall back to a different source.
- Recording health now updates the helper state after startup failure instead of leaving “Host: Starting” visible. Background Replay retains the underlying startup error for review.
- The existing interface, recording options, microphone consent, clips and preferences are retained.

Quit Flick, then install **Flick-Setup.exe** over the current installation. Portable users should replace the complete extracted folder. Previous releases, assets and tags remain available.

This is an unsigned preview. Startup regressions are checked with an owned metadata-only helper; they do not record the desktop or exercise GPU/audio hardware. Automated Windows/native/media/installer and render checks accompany the build. Physical gameplay, GPU/audio and real desktop acceptance remain separate.
