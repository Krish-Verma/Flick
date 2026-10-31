# Flick 1.0.13 Preview 1 — Replay and Reliability Fixes (UNSIGNED)

- Fixed normal monitor/background Replay being rejected before the capture helper started. Source identity is verified on the current helper; diagnostic consent remains separate. Failed starts report the actual helper state, release reservations and permit an explicit retry.
- Save Clip and Replay toggle shortcuts remain available while Flick is in the tray. Hiding/minimizing ends unfinished shortcut editing without losing Settings drafts. Tray/global controls share diagnostic, pending-save, update and shutdown safeguards.
- A failed source-preference write keeps the selected source, saved preferences and active recorder aligned. Late export/thumbnail/audio-edit completion cannot restore removed or unlinked clips; failed unlink/removal writes roll back coherently.
- Library rescans stop their media workers before Replay or shutdown proceeds. Bounded folder discovery reports depth, directory and file limits honestly.
- Audio-mix preview positions now map to the original timeline for trim/markers. Cancelled trim drags restore the previous range without saving. Audio-device Refresh recovers after page navigation; Rename accepts Enter.
- Uninstall removes only explicitly selected app metadata and empty cache directories. Preview creation no longer sweeps or overwrites existing MP4s; fresh previews use bounded admission and preserve ambiguous/legacy files. When preview storage fills, export remains available.
- Packaging uses a fresh owned staging directory. Cached OBS runtime files are checked against the hash-verified pinned archive; stale or mixed upstream bytes cannot silently enter a package.

Quit Flick, then install **Flick-Setup.exe** over the current installation. Portable users should replace the complete extracted folder. Clips and preferences are retained. Previous releases, assets and tags remain available.

This is an unsigned preview. Metadata-only helper tests cover managed startup, tray commands and failure recovery without recording a desktop or using GPU/audio devices. Automated Windows/native/media/installer, UI render and matched synthetic diagnostics accompany the build. Physical gameplay/GPU/audio, actual OS shortcut presses, Discord, Narrator and per-monitor DPI acceptance remain separate.

Exact-source validation: Windows/Core/WPF tests, generated-media and native checks, 167 actual WPF render views, installer lifecycle and 24 matched synthetic UI runs. Synthetic diagnostics do not establish gameplay or normal-app idle performance. See linked workflow evidence.

Manual performance review: the 1,000/5,000-clip synthetic fixtures showed higher one-core CPU P95 (+22.2/+25.9 percentage points); the 5,000-clip query and settings P95 were +10.9/+19.9 ms versus the pinned 1.0.11 baseline. No CPU/latency acceptance threshold is defined by this diagnostic. These short, correlated samples require real desktop qualification; no normal-idle, gameplay or FPS improvement is claimed.
