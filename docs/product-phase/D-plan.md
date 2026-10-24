# D — Editing, organization and export speed

Status: implemented in the D workstream branch; integration and Windows acceptance gates pending. Owner: Flick implementation lead. Umbrella: #36. Updated: 2026-10-24.

## Existing behavior

The existing atomic library, imported ownership, immutable originals, trim/audio persistence and validated exports are preserved. D adds local organization, timeline navigation, optional bounded frames and fitted export shapes. Search uses cached availability instead of probing files for every keystroke. See [implementation and evidence](D-EDITING-ORGANIZATION.md).

## Technical boundary

ClipMetadata, ClipStorage, MainViewModel, MainWindow, TrimTimeline and ClipExporter policies/tests

## Slice tracker

- [ ] D1 — Favorites tags and game organization — implemented; focused policy tests pass, Windows interaction pending

- [ ] D2 — Timeline shortcuts and edit state — implemented; focused trim tests pass, Windows interaction pending

- [ ] D3 — Bounded preview navigation — implemented; decoder/cancellation tests authored and compiled, Windows execution pending

- [ ] D4 — Export workflow and aspect presets — implemented; focused policy tests pass, two real-media cases pending Windows execution

## D1 — Favorites tags and game organization

**Objective/user benefit and expected behavior:** Backward-compatible metadata favorites/tags/game identity; filter/search/sort by title/tag/game/date/source/favorite; offline references remain visible and immutable.

**Dependencies:** A1 provenance.

**Exact scope/likely files:** ClipMetadata, ClipStorage, MainViewModel, MainWindow, TrimTimeline and ClipExporter policies/tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Backward-compatible metadata favorites/tags/game identity; filter/search/sort by title/tag/game/date/source/favorite; offline references remain visible and immutable.

**Acceptance, automated and manual/visual tests:** Index roundtrip/recovery, imported/offline files, case-insensitive search, tag normalization, large synthetic library.

**Performance:** Avoid filesystem probes per search; keep list virtualization.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## D2 — Timeline shortcuts and edit state

**Objective/user benefit and expected behavior:** Keyboard seek, set trim in/out, undo/reset context, saved/unsaved indication and manual markers within source duration; keep originals immutable.

**Dependencies:** D1.

**Exact scope/likely files:** ClipMetadata, ClipStorage, MainViewModel, MainWindow, TrimTimeline and ClipExporter policies/tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Keyboard seek, set trim in/out, undo/reset context, saved/unsaved indication and manual markers within source duration; keep originals immutable.

**Acceptance, automated and manual/visual tests:** Trim/marker roundtrip, bounds, keyboard text-field focus, rapid selection and stale async work; WPF interaction.

**Performance:** No continuous decoder or heavyweight editor.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## D3 — Bounded preview navigation

**Objective/user benefit and expected behavior:** Optional lazy filmstrip from low-resolution thumbnails with cancellation, per-clip generation tokens and strict cache/memory limits; navigation remains usable without filmstrip.

**Dependencies:** D2.

**Exact scope/likely files:** ClipMetadata, ClipStorage, MainViewModel, MainWindow, TrimTimeline and ClipExporter policies/tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Optional lazy filmstrip from low-resolution thumbnails with cancellation, per-clip generation tokens and strict cache/memory limits; navigation remains usable without filmstrip.

**Acceptance, automated and manual/visual tests:** Rapid switching/cancel, offline/imported media, thumbnail failures, memory before/after on equal synthetic fixtures.

**Performance:** On demand only; no decoding during gameplay.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## D4 — Export workflow and aspect presets

**Objective/user benefit and expected behavior:** Recent valid exports/filter, useful progress/cancel/readiness and size limits; if feasible non-destructive Fit9:16 and1:1 presets, original-size default and accurate bitrate budget.

**Dependencies:** C3,D2.

**Exact scope/likely files:** ClipMetadata, ClipStorage, MainViewModel, MainWindow, TrimTimeline and ClipExporter policies/tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Recent valid exports/filter, useful progress/cancel/readiness and size limits; if feasible non-destructive Fit9:16 and1:1 presets, original-size default and accurate bitrate budget.

**Acceptance, automated and manual/visual tests:** Full decode/dimensions/audio/sync/size, cancellation cleanup, stale fingerprint invalidation, immutable original hashes.

**Performance:** Measure export time and output size on identical fixtures.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.
