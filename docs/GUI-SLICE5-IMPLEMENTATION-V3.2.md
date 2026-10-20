# Flick GUI Slice 5 — Sharing & Settings Consolidation

**Source:** https://github.com/Krish-Verma/Flick  
**Branch:** `feat/gui-slice5-sharing-settings-20261020`  
**Draft PR:** https://github.com/Krish-Verma/Flick/pull/6  
**Base:** verified source/CI Slice 4 at `d32027f0d0990bcd588d495cbeebf13609739f0a`; Windows CI https://github.com/Krish-Verma/Flick/actions/runs/39493121694 (90 offline PASS, 7 GPU tests SKIPPED, native and WPF builds green).  
**Scope:** Authorized GUI Slice 5 ONLY. All ancestor PRs remain draft and must not be merged or released without owner consent.

## Product and user experience contract

Flick is a **free, open-source, lightweight Windows game clipper**. Manual replay capture, the saved-clip notification/sound, in-place imported libraries, the precise trim timeline, source-aware export audio, and actual Discord file dragging stay available. Capture always starts DISARMED. No new cloud, accounts, telemetry, AI engines, permanent background rendering, game injection or paid dependency is added here.

### Export dock

- Fixed export area remains below the independently scrollable player/editor; the old long 20/50/100/custom radio row and colorful permanent webhook button are removed.
- One **Export for Discord** heading, small caption, one size drop-down and one primary **Export Clip** button. **20 MB is the default**, with 10/50/100 MB presets and a **1–250 MB custom** entry. Caps are decimal bytes, not MiB. Discord upload rules can vary by destination or account; a cap is not an upload guarantee.
- Only genuinely available clips can be exported. While a single export runs, the start button is disabled, an **indeterminate progress strip** shows the actual stage without inventing a time estimate, and **Cancel Export** sends cancellation to the bounded FFmpeg worker.
- The previously selected clip and size are snapped at export start. A completed export is recorded on **that original clip**, not whichever video the user subsequently selected. The Ready tile is visible only if the current selection's source, trim, audio fingerprint, MP4 filename, actual byte length and configured size cap still match the verified media export state.
- Ready state shows a small drag source tile with the actual .mp4 file path, **Copy File**, and **Open Folder**. Native OLE drag begins only after a mouse movement threshold; simply pressing the mouse does not initiate a drag. Copy uses Windows FileDrop clipboard format; no Discord login or auto-send is needed.
- Editing source, trim, audio or size invalidates outdated export status. A ready file cannot be faked through UI state; a missing, renamed, empty or size-altered file must not be offered for drag/copy. The exporter already validates video/audio codecs, cap and full decode before promotion; last-mile readiness also checks current metadata, fingerprint and bytes. It is **not** a new full media re-decode on every mouse movement.
- Optional webhook posting is under collapsed **Advanced: Direct Posting**. It must be enabled in Settings and is manually confirmed separately on every send. It is never triggered by Export, drag, copy or Save Clip.

### Six-category Settings

The older stacked mixed-style dialog is replaced by **one WPF category-window** reached by the slim Settings rail or tray. The navigation is **Recording, Audio, Shortcuts, Storage, General, Sharing**, with a small constant category rail and only one readable scrollable content section at a time. The active category is softly tinted with Flick's existing graphite/violet tokens. This is not a separate new process or web frontend. It remains a modal settings window; the underlying recorder stays active while the user browses preferences.

- **Recording:** 15–120 sec replay length, Light/Balanced/Smooth NVENC presets, truthful compressed buffer estimate, no auto recording.
- **Audio:** System output loopback fallback is identified as such; microphone remains independently opt-in (off by default). Unsupported independent game/Discord routing is **not** invented.
- **Shortcuts:** Capture Toggle and Save Replay combinations can be recorded by pressing real key chords, with validation and rollback when a replacement hotkey conflicts.
- **Storage:** independent Saved Clips and Export folders, browser entry and manual linked-folder management. Changing destinations affects only new files; no migration or deletion.
- **General:** start with Windows, minimize to tray, small temporary full-Flick-logo Clip Saved overlay and sound toggles, preview notification. Both feedback toggles default on.
- **Sharing:** default local size cap, collapsed advanced webhook, explicit enable switch and encrypted URL/label. Merely setting a URL does not opt in to direct posting.
- Changes are staged in a separate configuration object, validated as a whole, and committed to disk before updating the running Settings object. Cancel/Restore Defaults do not silently write settings. Windows startup registry updates are explicit; on settings-save failure the previous registry value is restored where possible.

### Performance and accessibility

- Reuse WPF .NET 10, existing shared Segoe UI Variable typography, 4/8/12/16/24 spacing and muted violet/graphite components, with native ComboBox/controls and visible focus. No extra frontend runtime, shader or polling worker.
- At 1240×760 the export dock must not crowd the 16:9 preview; at 1000×650 and 125/150/200% DPI the editor and settings content must remain reachable without clipping. XAML and automation attributes are testable but actual DPI proof needs Windows screenshots.
- Tabs/shortcuts/size entry, Settings navigation, disabled states, Cancel Export and manual file drag must be keyboard/mouse tested on the owner's PC.
- Automatic webhook posts, auto-start recording, cloud uploading, persistent status overlays and a full media editing suite remain excluded.

## Qualification boundaries

GitHub Actions validates source compilation, native MinGW/libobs compilation, unit and static contract tests and WPF self-contained publish. It does NOT verify live NVENC, real Discord desktop drag, Windows clipboard behavior, click-through overlay, DPI, performance or user media. Execute `docs/GUI-SLICE5-ANTIGRAVITY-HANDOFF.md` for the hardware acceptance.

**Gate:** Keep PR #6 draft and private until owner approves; do not begin Slice 6, merge or publish in this workstream.
