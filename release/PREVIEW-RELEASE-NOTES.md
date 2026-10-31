# Flick 1.0.13 Preview 1 — Replay and Reliability Fixes (UNSIGNED)

- Fixed normal monitor/background Replay being rejected before the capture helper started. Source identity is verified on the current helper; diagnostic consent remains separate. Failed starts report the actual helper state, release reservations and permit an explicit retry.
- Save Clip and Replay toggle shortcuts remain available while Flick is in the tray. Hiding/minimizing ends unfinished shortcut editing without losing Settings drafts. Tray/global controls share diagnostic, pending-save, update and shutdown safeguards.
- A failed source-preference write keeps the selected source, saved preferences and active recorder aligned. Late export/thumbnail completion cannot restore removed or unlinked clips.
- Library rescans stop their media workers before Replay or shutdown proceeds. Bounded folder discovery reports depth, directory and file limits honestly.
- Audio-mix preview positions now map to the original timeline for trim/markers. Cancelled trim drags restore the previous range without saving. Audio-device Refresh recovers after folder navigation; Rename accepts Enter.
- Uninstall removes only explicitly selected app metadata and empty cache directories; nonempty folders are retained to protect custom media locations.
- Packaging uses a fresh owned staging directory. Cached OBS runtime files are checked against the hash-verified pinned archive; stale or mixed upstream bytes cannot silently enter a package.

Quit Flick, then install **Flick-Setup.exe** over the current installation. Portable users should replace the complete extracted folder. Clips and preferences are retained. Previous releases, assets and tags remain available.

This is an unsigned preview. Metadata-only helper tests cover managed startup, tray commands and failure recovery without recording a desktop or using GPU/audio devices. Automated Windows/native/media/installer, UI render and matched synthetic diagnostics accompany the build. Physical gameplay/GPU/audio, actual OS shortcut presses, Discord, Narrator and per-monitor DPI acceptance remain separate.
