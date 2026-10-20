# Flick GUI Slice 4 — Player, Trim & Audio (v3.2)

**Repo:** https://github.com/Krish-Verma/Flick  
**Draft PR:** https://github.com/Krish-Verma/Flick/pull/5  
**Branch:** `feat/gui-slice4-player-trim-audio-20261020`  
**Base:** Verified Slice 3 `4bca99dffeba9ff585a8120722032a35daeff920`, CI https://github.com/Krish-Verma/Flick/actions/runs/39463761112 (61 pass, 7 hardware skipped, native and WPF builds green).

**Authorization boundary:** Slice 4 only. The design-system, compact shell, Clip Saved toast/sound, imported local media and Discord export workflow are preserved. Do not merge PRs 1–5, publish a release, or start Slice 5 without owner approval.

## UX changes

- Existing editor reorganized as one scrollable content column (selected clip title, large dark 16:9 playback surface, compact transport, trim, collapsed audio) plus **sticky validated Discord export dock**. The clip library and compact capture controls remain functional.
- Preview mode is a small contextual **Original / Export Preview** dropdown, with a separate external player escape hatch. Only a currently valid, verified MP4 is offered as Export Preview; any changed trim/audio target invalidates Ready and returns to Original.
- Native Windows WPF `MediaElement` plays the original where supported, without adding a new media framework/process.
- When native MKV playback fails, the user sees an actionable fallback: Open External Player or **Create Local Preview**. Only a real click starts an owned FFmpeg process; no background transcode is started for browsing clips.
- Local Preview output is a separate cached MP4 under `%LOCALAPPDATA%/LocalClip/PreviewCache`, never the original or export directory. H.264 small originals may remux; otherwise explicitly bounded 540p H.264/AAC low-resource transcode. If remuxed MP4 fails too, an explicit **Try Compatible Preview** reencodes once.
- Preview is limited to the **first 180 seconds** of a longer imported recording and a **120 MB file cap**, with 90-second deadline, cancellation, Job Object ownership, ffprobe verification and cache pruning on demand. Truncated local preview is labeled and must not be confused with the full immutable original. Live gameplay capture or export blocks preview encoding, and capture startup cancels an existing request.
- Video decoding stops when Flick hides to tray or when capture starts to prioritize game performance; revisiting Clips restores the original source. No permanent preview worker, polling or shader is added.

## Trim behavior

- `Views/Components/TrimRangeControl`: one 30px track with 2 clearly distinct draggable handles; each supports Tab focus, arrow keys in 0.1s steps, Ctrl+arrow in 1s steps, Home/End, and mouse dragging.
- Direct precise entry supports seconds, M:SS.ff and H:MM:SS.ff. Invalid/NaN/out-of-range values are rejected with restoration of the last valid edit.
- Minimum selected duration is **0.25 seconds**; all commits pass `TrimValidator` and finite checks. Reset restores the complete original. The selected duration is always visible.
- Dragging updates the control immediately; metadata is persisted **once on drag completion**, key action, or validated time entry, never per mouse-motion frame. The underlying MKV/MP4 original stays unchanged.
- Existing export fingerprint invalidation ensures even tiny trim changes require a fresh MP4 export. Exporting/offline clips cannot be trimmed.

## Audio behavior

- `Export Audio` is collapsed by default. Expansion shows only *actual* source tracks (no invented Game or Discord isolation) with Mute, 0–200% gain, current percentage and Reset Mix.
- `AudioTrackInfo` supports `INotifyPropertyChanged` for mute/gain, so sliders and labels update together. The UI invalidates stale export status **immediately**, but persists edits after a bounded 450ms inactivity debounce, flushing when changing selected clips or quitting.
- Exported MP4 is the accurate way to listen to the new mix. **Original playback is not silently modified.** Clip selection does not overwrite other clips' audio state.
- No additional audio processing while preview is paused. Source metadata is managed locally by the existing storage layer. The original combined-track duplication guard is retained.

## Design system and constraints

- Uses Slice 1 graphite/frosted WPF tokens, Segoe typography, consistent spacing, compact controls, and Title Case. No new animation engine, webview, AI process or vendor account.
- Keeps original capture source, Start/Stop and Save Last N Seconds hotkeys, tray feedback, local gallery/import/storage, and Discord drag/copy behavior.
- Screen-width responsiveness relies on scrollable editor and 245 DIP preview pane; GUI must be reviewed for overflow at 1000x650 DIPs and scaling 100–200%. The export dock remains available at the bottom.
- `MediaProber` now contains the optional conversion worker; no native libobs recording changes.

## Testing

Code/tests/CI check compiler and static contracts but **do not prove** actual MKV/MP4 playback, preview audio, GPU FPS, WPF layout, playback fallback, 50-cycle trimming or no-orphan workers on user's Windows GPU system. These require `docs/GUI-SLICE4-ANTIGRAVITY-HANDOFF.md`.

Do not present unrun hardware, full-screen focus, Discord desktop or high-DPI checks as passing.
