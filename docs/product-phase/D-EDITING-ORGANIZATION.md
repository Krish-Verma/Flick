# D: local organization, editing and export

Implemented in `feat/editor-library-complete-20261024` on 2026-10-24. This is implementation and focused-check evidence, not release or hardware qualification. Parent integration owns the combined Windows, media, visual, installer and physical acceptance gates. No release was published by this workstream.

## Library

The existing library index receives backward-compatible fields for favorite, tags, game label, manual markers, export aspect and the most recent successful export. Missing legacy fields retain defaults. Invalid new fields use the existing preserve-and-recover index policy; the original JSON is not silently rewritten. Imported files stay referenced in place and normal removal never deletes them.

Clip details offer an explicit Save action. Failed writes retain the previous metadata and show a local error; changing selection or quitting with unsaved details asks before discarding them. Favorites save immediately. Tags are case-insensitive and de-duplicated, with at most 12 tags of 32 characters. Game labels are at most 80 characters. These organizational edits do not invalidate a media export.

Search matches all terms across title, game/source, tags and ISO date (`yyyy-MM-dd`); `#tag` is accepted. Source, game, favorite and 7/30-day filters compose. Sort choices are newest, oldest, game (with group headings), title and recently exported. Offline references remain searchable. Availability refreshes during discovery/rescan, rather than performing disk checks on every search keystroke. The list retains virtualization. Reconnect a source and choose Rescan to refresh its availability.

## Timeline

The Trim drawer keeps the existing two handles and precise time entry, and adds a visible playhead, click-to-seek, Set In, Set Out, bounded trim undo and manual markers. Markers are source-relative times, not automatic game events. Up to 32 markers are stored per clip. Click a marker to seek; its context menu removes it. Metadata writes leave the original media unchanged.

When the Trim drawer has keyboard focus outside text, combo, slider, handle and library inputs: Left/Right seek one second; Ctrl+Left/Right seek five; Home/End seek to source bounds; I/O set trim in/out; M adds a marker; Ctrl+Z undoes the most recent trim. Handle keyboard controls and validated time entry remain available. The undo history is bounded to 20 changes for the current selection and clears on selection change. Dirty, saved, reverted and failed trim states are explicit. Failed persistence restores the previous range.

## Optional preview frames

Preview frames are requested explicitly in the Trim drawer; no new frame worker runs automatically during recording. Six sequential thumbnail operations have a 30-second total request deadline, 15-second worker deadlines and bounded cancellation cleanup. Selecting a different clip, closing the drawer, hiding the app, starting recording/diagnostics/export or quitting cancels the request. Generation and clip identity checks reject stale results. Capture/export starts wait for owned frame work to settle; an unconfirmed worker exit blocks new work and requests an app restart.

Each JPEG is limited to 512 KiB and 320×180 source dimensions. Final frozen bitmaps are independent copies at no more than 160×90 BGRA, so six retained display frames use at most **345,600 bytes** of decoded pixel data. They do not retain the larger decoder or open streams. The unique temporary frame directory is removed on completion/cancellation; originals are never altered. Navigation remains usable when loading fails or the file is offline.

The UI reports decoded pixel bytes and a before/after managed-memory observation. This observation includes concurrent app allocations and is not process working set or a gameplay benchmark. The WPF test measures the same generated six-frame fixture; the final Windows receipt measured345600 retained pixel bytes and4136024→6215232 managed bytes, including concurrent allocations. No measured physical-memory improvement is claimed yet.

## Exports and Share integration

The default shape remains Original. Optional **Fit 9:16** and **Fit 1:1** preserve the full picture inside a black canvas; they do not crop. Output dimensions are even, bounded by the bitrate-derived resolution, and shaped canvases have exact ratios. Scaling honors sample aspect ratio and emits square pixels. FFmpeg 9.0.2 documents the `scale` filter's `reset_sar` behavior in its [official filter source](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/doc/filters.texi).

Aspect participates in the export fingerprint and filename. Changing trim, audio or shape makes an old export unavailable for sharing until the new settings validate. Existing byte-cap verification, atomic destination promotion, bounded retries/CPU fallback, cancellation cleanup, media probing and full-decode verification remain in effect. Shaped results additionally require the expected dimensions. The original hash is checked by the new real-media cases.

Recent exports are bounded to the newest 20 currently valid entries, considering at most 40 stored candidates. They re-use `ReadyShareFile`, including source identity, fingerprint, destination and byte-cap checks. The VM exposes `RecentExports`, `SelectRecentExport`, `ExportAspectChoices`, `ExportAspectPreset` and `ExportAspectDescription`; the parent E workstream owns their final Share-pane presentation. Discord limits remain user-selected decimal MB caps, not claims about every Discord destination.

## Evidence and remaining gates

- Core model/policy commit: `2cf7240c28aeb9edcc94fcbf52f20fac2dadeee1` (subsequent UI/lifecycle work is a separate local commit).
- Focused portable Core run: **73 passed, 0 failed, 0 skipped**, excluding the Windows-only DPAPI baseline test. Receipt: `artifacts/workstream-d/d-focused-portable-final.trx`. An earlier expanded run executed that baseline on macOS and reported its expected platform exception; this was not a product regression.
- Full Release solution cross-build on macOS: warnings treated as errors; final exact-source result is recorded in the delivery message. A cross-build does not execute WPF.
- New `AspectMediaWorkflowTests.FitVerticalPreservesPictureAudioAndOriginal` and `.FitSquarePreservesPictureAudioAndOriginal`: executed and passed in final Windows batch40102045722. Both generate test video/sine audio, CPU-encode the fitted export, check size, H.264/AAC/dimensions/SAR/padding/AV alignment, independently decode it and recheck the original SHA-256. They require the existing explicit pinned media gate; missing tools fail when enabled.
- New `FilmstripFrameTests.SixGeneratedFramesHaveBoundedIndependentPixelStorage`: executed and passed in final Windows batch40102045722. Uses generated JPEGs, exact decoded-byte accounting, disposed-stream independence, portrait sizing and oversize rejection.
- The existing single-Application WPF staging test now also runs `OrganizationEditorAssertions`: metadata persistence and write-failure rollback, trim undo, marker identity/bounds, dirty selection refusal, offline search, invalid precise range rejection, and cancellation of a delayed old clip's frame worker before any stale image may publish. Executed and passed in final Windows App9 suite.
- Final automated suite, real FFmpeg cases,67 WPF renders, cancellation/rapid-state helpers and installer upgrade checks pass. Physical playback, actual display DPI/High Contrast, Discord and equivalent gameplay performance remain open; see FINAL-REPORT.md.

## Integration notes

Merge C's audio role/routing additions with D's aspect-aware fingerprint and validation; retain both. C's delayed audio-persistence methods should publish matching pending/saved/error editor feedback. E owns the Share preview/tile/footer markup and FileDrop behavior. D intentionally does not redesign that region or change clipboard/FileDrop contracts. Root owns the combined qualification count and release authority.
