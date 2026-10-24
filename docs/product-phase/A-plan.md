# A — Recording health and diagnostics

Status: A1/A2 implemented/tested/integrated through PR44–46; physical qualification remains open. A3 in progress; A4 planned. Owner: Flick implementation lead. Umbrella: #36. Updated: 2026-10-24.

## Existing behavior

Engine events are partly visible, but a transient footer replaces errors; polling overlaps and status failures can invent Off. Validated save notification and atomic indexing already exist.

## Technical boundary

Health models, CaptureHostClient, MainViewModel, MainWindow, HotkeyManager, Diagnostics, tests

## Slice tracker

- [x] A1 — Truthful recording state and retained health

- [x] A2 — Bounded private diagnostics and redacted export

- [ ] A3 — Consented diagnostic test and recovery

- [ ] A4 — Reliability regression and operating guide

## A1 — Truthful recording state and retained health

**Objective/user benefit and expected behavior:** Replace optimistic/overwritten health with engine state plus independent save activity; serialize health polls, do not spawn a helper for queries, correlate saves and snapshot source provenance; show source/profile/buffer/host/hotkeys/last error and last validated clip.

**Dependencies:** None.

**Exact scope/likely files:** Health models, CaptureHostClient, MainViewModel, MainWindow, HotkeyManager, Diagnostics, tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Replace optimistic/overwritten health with engine state plus independent save activity; serialize health polls, do not spawn a helper for queries, correlate saves and snapshot source provenance; show source/profile/buffer/host/hotkeys/last error and last validated clip.

**Acceptance, automated and manual/visual tests:** Fake event sequences, out-of-order/duplicate saves, rejected status, helper exit, retained errors after stop, multiple saves; WPF health inspection.

**Performance:** One bounded in-memory health record; no extra background helper.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## A2 — Bounded private diagnostics and redacted export

**Objective/user benefit and expected behavior:** Persist allowlisted diagnostic events locally with bounded count/bytes/age; export a user-chosen text/JSON troubleshooting bundle without raw host stderr, clips, paths, titles, credentials or webhook data.

**Dependencies:** A1.

**Exact scope/likely files:** Health models, CaptureHostClient, MainViewModel, MainWindow, HotkeyManager, Diagnostics, tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Persist allowlisted diagnostic events locally with bounded count/bytes/age; export a user-chosen text/JSON troubleshooting bundle without raw host stderr, clips, paths, titles, credentials or webhook data.

**Acceptance, automated and manual/visual tests:** Retention, malformed journal recovery, concurrent writes, export secret/path canaries, inaccessible folder; user export flow.

**Performance:** Bounded writes on events, never per frame.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## A3 — Consented diagnostic test and recovery

**Objective/user benefit and expected behavior:** Explicit confirmation names selected source/audio, starts replay only on consent, saves an actual short test clip, validates through storage/media pipeline, reports profile/readiness/save elapsed times and releases resources; preserve normal capture and pending saves.

**Dependencies:** A1,A2.

**Exact scope/likely files:** Health models, CaptureHostClient, MainViewModel, MainWindow, HotkeyManager, Diagnostics, tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Explicit confirmation names selected source/audio, starts replay only on consent, saves an actual short test clip, validates through storage/media pipeline, reports profile/readiness/save elapsed times and releases resources; preserve normal capture and pending saves.

**Acceptance, automated and manual/visual tests:** Cancel/refuse/active-session rejection, failure cleanup, missing encoder/target/storage, host crash/restart; synthetic validation and available Windows session.

**Performance:** User-invoked bounded operation; hardware timing unverified without PC.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## A4 — Reliability regression and operating guide

**Objective/user benefit and expected behavior:** Exercise repeated startup/shutdown/save, hotkey registration conflicts, missing target, host exits, failed storage and recovery. Document usable recovery steps and precise remaining physical gates.

**Dependencies:** A1-A3.

**Exact scope/likely files:** Health models, CaptureHostClient, MainViewModel, MainWindow, HotkeyManager, Diagnostics, tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Exercise repeated startup/shutdown/save, hotkey registration conflicts, missing target, host exits, failed storage and recovery. Document usable recovery steps and precise remaining physical gates.

**Acceptance, automated and manual/visual tests:** Offline/Windows/synthetic media tests, race and failure sequences, no false success; host process and real hotkey tests where accessible.

**Performance:** Compare equivalent idle/record/save measurements, do not infer FPS from CI.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.
