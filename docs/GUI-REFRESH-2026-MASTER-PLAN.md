# Flick UI Refresh — Seven-Slice Implementation Plan (2026-10-23)

Status: Approved direction; implementation proceeds through independently reviewable GitHub PRs. Refactor existing Windows WPF UI, not the recording engine.

## Design and functional contract

- **Single native Flick main window** for Clips, Player, Trim, Audio, Share, Settings, Connected Folders, and Flick-owned confirmations. Operating-system file/folder pickers, actual Windows uninstall, and external OAuth browser flow may open provider-owned dialogs/windows; no separate Flick Settings or Connected Folders window.
- **Video first:** dominant gameplay preview; compact playback, time, and original/export source control; tool icons for Trim, Audio, and Share that display contextual workspaces only when invoked.
- **Premium and cohesive:** dark graphite/lavender titlebar including Windows controls; centered original Flick logo with circular optical framing; slim themed scrollbars, popup menus and controls. Maintain resize, Windows Snap, keyboard focus and high contrast. Do not remove functionality to fake minimalism.
- **Shortcuts:** show Save Last 30s (duration updates with replay length), Start / Stop Replay. Saved shortcuts are read-only and muted; Change -> Record Shortcut -> explicit Save or Cancel. Capture keyboard input only while editing, reject collisions/reserved/invalid combinations. No new idle listeners or accidental re-registration.
- **Share:** share action opens a compact in-window export panel; preserve normal local H.264/AAC file export, 20 MB default and other bounds, native drag/copy/open folder; Discord is an explicit secondary option. Preserve opt-in webhook with DPAPI and confirmation. Real Discord OAuth/linking only after secure architecture review, never ship a shared bot secret/client secret or post automatically.
- **Performance:** preserve virtualized clip library, bounded 256 px decoded thumbnail, lazy preview/media decoder and offline-first native capture host. Avoid heavy blur, new permanent overlays/timers/duplicate media controls; measure RAM/FPS without invented metrics.
- **Release boundaries:** Do not merge, overwrite signed releases, make repo public, disable Smart App Control or claim AMD/Intel-only/ARM support. Current blocker is trusted signing; UI work must not weaken it. Qualified preview is Windows 11 x64 NVIDIA NVENC.

## Milestones / isolated slices

| Slice | Source scope | Acceptance gate | AntiGravity hardware check |
|---|---|---|---|
| R1 — In-window navigation | Convert Settings + Connected Folders from separate WPF windows to embedded UserControl pages and navigation. Preserve Save/Cancel, folder scan cancellation and replay while navigating. | No app-owned ShowDialog for Settings/Folders; selected clip, settings persistence and capture state preserved; no eager media decoder when page hidden; minimum 720x480 layout | Clips->Settings->Folders->Back, Cancel and Save, active capture, connected folder import, tray restore, 100–200% DPI |
| R2 — Chrome, logo, scroll & menus | Dark custom Windows caption, centered/clipped logo, slim themed ScrollBar / ComboBox / ContextMenu / Popup. | No white app titlebar/menus/scrollbars; native resize, dragging, minimize, maximize, Snap, focus and high-contrast preserved | Real screenshots at 100/125/150/200% scales, keyboard nav and Windows Snap |
| R3 — Video-first player | Large preview and compact player toolbar; remove permanent large Trim/Discord blocks, add focused state and empty player state. | Playback, scrub, selected clip, original/export/local preview and errors preserved; no eager decoder | Playback, seeking, clip switching, FPS/RAM/first-play delay |
| R4 — Contextual edit surfaces | Trim/Audio/Original panels open on explicit toolbar action in same window, close to uncluttered preview, themed confirmation. | Non-destructive trim, audio channel gains and mute, reset and saved state preserved | Real trim scrubbing, audio mix, error and recoveries |
| R5 — Share & Discord | Share toolbar opens in-window MP4 export/file/Discord flow; retain webhook, file-size settings, readiness, cancellation and data privacy. OAuth gated on reviewed broker architecture. | No automatic sending; consistent file size caps, FFmpeg decoding; no embedded shared tokens | 10/20/50/100/custom MB, Discord composer file drop, no actual sending |
| R6 — Shortcut recording UX | Clear labels, view-only shortcut chip, Change/Record/Save/Cancel capture lifecycle and validation. | No accidental edit or persistent new listener; conflicts rejected; Windows startup hotkeys retained until explicit Save | Real keyboard combos, Esc, duplicates, incomplete chord, settings persistence |
| R7 — Consistency/performance QA | Bring remaining Flick-owned MessageBox surfaces into themed main-window overlays; clean old names; full a11y, resource QA, final documentation. | One-window invariant, full CI, no original file mutation, 0 XAML errors, consistent resource budgets | Real Windows screenshots, hardware capture, drag/drop, FPS/1% low, RAM/handles and approved soaks |

## Per-slice engineering protocol

- One isolated branch and PR for each slice, based on verified previous source state; exact SHA pinned in documentation.
- Coding owner implements all source and docs. AntiGravity is TEST ONLY, uses safe synthetic fixtures, submits a complete PASS/FAIL/BLOCKED report, and never modifies source or publishes.
- Run Release WPF compile with warnings-as-errors, all 120 offline regression tests (7 hardware gated in hosted CI), GUI structural scripts 1–6 plus slice-specific static acceptance; native libobs compile; package smoke as needed. Hardware CI skips are not real-GPU acceptance.
- Review correctness, UX, data preservation, accessibility, performance and security each slice. Physical QA tests Windows 11/NVIDIA RTX recording and real 100/125/150/200% scale; do not invent FPS or other measurements.
- No releases, merges or repo visibility changes during UI refresh without explicit release authorization; Smart App Control signing remains a separate release blocker.

## R1 detailed acceptance

1. MainWindow remains the only top-level app-owned management window. Settings/ConnectedFolders existing XAML content is converted to UserControl and hosted in MainWindow's central pane; active page is created only when opened.
2. Preserve Settings staged edits/atomic Save and Cancel behavior, hotkey conflict rollback, mic opt-out capture stop and separate uninstall confirmation.
3. Connected-folder import is non-destructive. If opened from Settings, Close returns to Settings; if opened from Clips, Close returns to Clips. Leaving an active scan requests cancellation, never stops replay.
4. User returns to the same clip/editor state; no decoder runs offscreen. Capture bar/status and tray remain alive during page navigation. No extra Flick-owned windows.
5. Keep existing folder picker an OS-owned dialog; remove only Flick-owned windows. Future R7 replaces remaining Flick-owned confirmations.
6. Add source regression tests for XAML UserControl roots, no modal management ShowDialog and one-window hosting; independent Windows CI verifies generated XAML/code-behind binding.
