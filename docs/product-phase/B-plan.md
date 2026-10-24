# B — Optional game detection and replay management

Status: planned. Owner: Flick implementation lead. Umbrella: #36. Updated: 2026-10-24.

## Existing behavior

Capture sources are manually enumerated by the native helper. No approved game profiles or automatic replay lifecycle exists. Manual startup defaults Off.

## Technical boundary

GameDetection policies and Windows window enumerator, Settings, MainViewModel, SettingsPage, native target validation, tests

## Slice tracker

- [ ] B1 — Allowlisted detection policy

- [ ] B2 — Explicit game profile settings

- [ ] B3 — Lifecycle automation and manual override

- [ ] B4 — Edge cases and qualification

## B1 — Allowlisted detection policy

**Objective/user benefit and expected behavior:** Pure deterministic decision engine matches user-approved executable identity to eligible visible top-level windows, excludes Flick and ambiguous/multiple games; no desktop fallback.

**Dependencies:** A1.

**Exact scope/likely files:** GameDetection policies and Windows window enumerator, Settings, MainViewModel, SettingsPage, native target validation, tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Pure deterministic decision engine matches user-approved executable identity to eligible visible top-level windows, excludes Flick and ambiguous/multiple games; no desktop fallback.

**Acceptance, automated and manual/visual tests:** Disabled/empty profiles, duplicate/exact paths, multiple windows/games, minimize/Alt+Tab/restart; no capture decision for unknown identity.

**Performance:** Low frequency scan only when opted in.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## B2 — Explicit game profile settings

**Objective/user benefit and expected behavior:** Opt-in global switch and profiles chosen from actual running windows; source name, replay profile and microphone consent remain explicit; malformed/legacy settings migrate to disabled.

**Dependencies:** B1.

**Exact scope/likely files:** GameDetection policies and Windows window enumerator, Settings, MainViewModel, SettingsPage, native target validation, tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Opt-in global switch and profiles chosen from actual running windows; source name, replay profile and microphone consent remain explicit; malformed/legacy settings migrate to disabled.

**Acceptance, automated and manual/visual tests:** Load/save migration and invalid allowlists; keyboard settings flow, enable/disable confirmation.

**Performance:** No work while disabled; bounded profiles.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## B3 — Lifecycle automation and manual override

**Objective/user benefit and expected behavior:** Start exact approved window when eligible; stop/release on exit, disable or identity loss; serialize start/stop with poll; manual stop suppresses auto-start until exit/restart, never retarget a manual capture.

**Dependencies:** A1,B1,B2.

**Exact scope/likely files:** GameDetection policies and Windows window enumerator, Settings, MainViewModel, SettingsPage, native target validation, tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Start exact approved window when eligible; stop/release on exit, disable or identity loss; serialize start/stop with poll; manual stop suppresses auto-start until exit/restart, never retarget a manual capture.

**Acceptance, automated and manual/visual tests:** Launch/exit/restart, concurrent save, duplicate tick, stale PID/HWND, app restart, two games, manual start/stop; fake lifecycle plus real windows when available.

**Performance:** Single active session, no orphan helper.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## B4 — Edge cases and qualification

**Objective/user benefit and expected behavior:** Visible reason when no eligible game; native exact source binding, unsupported/fullscreen behavior and multi-display handling; reproducible Windows test scenarios.

**Dependencies:** B3.

**Exact scope/likely files:** GameDetection policies and Windows window enumerator, Settings, MainViewModel, SettingsPage, native target validation, tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Visible reason when no eligible game; native exact source binding, unsupported/fullscreen behavior and multi-display handling; reproducible Windows test scenarios.

**Acceptance, automated and manual/visual tests:** Minimize/restore, Alt+Tab, source change, elevated process refusal, crash, monitor rearrangement, overhead baseline.

**Performance:** Physical games/anti-cheat compatibility remains per tested title, no universal claim.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.
