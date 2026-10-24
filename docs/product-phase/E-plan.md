# E — Share screen redesign and file transfer

Status: planned. Owner: Flick implementation lead. Umbrella: #36. Updated: 2026-10-24.

## Existing behavior

Share is a bottom-right max600DIP panel with tiny exported-file row and path, no prominent thumbnail/title/duration. Actual guarded FileDrop and clipboard already exist; fixture renders cover only unexported state.

## Technical boundary

MainWindow SharePanel/code, MainViewModel share state, shared theme (only needed tokens), Flick.UiRender, STA transfer tests

## Slice tracker

- [ ] E1 — Research and share state specification

- [ ] E2 — Preview tile and brighter hierarchy

- [ ] E3 — Whole-tile real file transfer

- [ ] E4 — State interaction and accessibility QA

## E1 — Research and share state specification

**Objective/user benefit and expected behavior:** Use primary Steam/SteelSeries/Medal references and actual WPF baseline; define empty/unexported/exporting/failed/offline/stale/ready states and original vs export metadata.

**Dependencies:** Phase0.

**Exact scope/likely files:** MainWindow SharePanel/code, MainViewModel share state, shared theme (only needed tokens), Flick.UiRender, STA transfer tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Use primary Steam/SteelSeries/Medal references and actual WPF baseline; define empty/unexported/exporting/failed/offline/stale/ready states and original vs export metadata.

**Acceptance, automated and manual/visual tests:** Record dated sources, screenshots provenance and real file-transfer differences; specification review.

**Performance:** No runtime dependency.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## E2 — Preview tile and brighter hierarchy

**Objective/user benefit and expected behavior:** Share-only graphite/lavender surface, real lazy thumbnail, title/duration/MP4 size/readiness, original vs exported distinction and compact primary action. Purple accent tasteful, no whole-app reskin.

**Dependencies:** D4,E1.

**Exact scope/likely files:** MainWindow SharePanel/code, MainViewModel share state, shared theme (only needed tokens), Flick.UiRender, STA transfer tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Share-only graphite/lavender surface, real lazy thumbnail, title/duration/MP4 size/readiness, original vs exported distinction and compact primary action. Purple accent tasteful, no whole-app reskin.

**Acceptance, automated and manual/visual tests:** Before/after real WPF fixture renders, narrow720p/1080p/1440p, DPI100/125/150/200 and high contrast.

**Performance:** Reuse existing thumbnail, freeze image and cap decode.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## E3 — Whole-tile real file transfer

**Objective/user benefit and expected behavior:** Entire ready clip tile initiates native Windows DataFormats.FileDrop after last-mile file/fingerprint validation; Copy File keyboard-friendly fallback; no text/link masquerading as file.

**Dependencies:** E2.

**Exact scope/likely files:** MainWindow SharePanel/code, MainViewModel share state, shared theme (only needed tokens), Flick.UiRender, STA transfer tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Entire ready clip tile initiates native Windows DataFormats.FileDrop after last-mile file/fingerprint validation; Copy File keyboard-friendly fallback; no text/link masquerading as file.

**Acceptance, automated and manual/visual tests:** STA clipboard IDataObject paths, drag threshold/guards, stale/removed file, keyboard copy/open. Actual Discord desktop gate if accessible.

**Performance:** No file copies in memory and no upload.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## E4 — State interaction and accessibility QA

**Objective/user benefit and expected behavior:** Progress/failure/retry/offline/stale views, drag hover feedback, focus/navigation/automation labels, concise limits; preserve gallery/editor functions.

**Dependencies:** E3.

**Exact scope/likely files:** MainWindow SharePanel/code, MainViewModel share state, shared theme (only needed tokens), Flick.UiRender, STA transfer tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Progress/failure/retry/offline/stale views, drag hover feedback, focus/navigation/automation labels, concise limits; preserve gallery/editor functions.

**Acceptance, automated and manual/visual tests:** WPF renderer matrix+automated readiness/error tests; actual mouse/keyboard/DPI/HC session when available.

**Performance:** Compare same WPF fixture memory/render; no gaming FPS inference.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.
