# C — Real audio separation and controls

Status: planned. Owner: Flick implementation lead. Umbrella: #36. Updated: 2026-10-24.

## Existing behavior

Native whole-device loopback plus optional mic produces Combined/System/Mic buses. GameProcessId, DiscordProcessId and AudioMode do not currently route native sources. OBS32.2.2 has process output capture; tracks are not proof of isolation.

## Technical boundary

native main.cpp and audio routing policy, CaptureHostClient/Protocol, SettingsPage, audio mix rules, media exporter/tests

## Slice tracker

- [ ] C1 — Capability and routing contract

- [ ] C2 — Actual source selection and capture

- [ ] C3 — Honest editor and export mix

- [ ] C4 — Audio isolation acceptance

## C1 — Capability and routing contract

**Objective/user benefit and expected behavior:** Inspect pinned OBS process source and supported Windows builds; specify Combined/Game/Voice/Mic bus map with exact process/window identity, actual hooked status and explicit fallback. Remove misleading claims.

**Dependencies:** A1.

**Exact scope/likely files:** native main.cpp and audio routing policy, CaptureHostClient/Protocol, SettingsPage, audio mix rules, media exporter/tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Inspect pinned OBS process source and supported Windows builds; specify Combined/Game/Voice/Mic bus map with exact process/window identity, actual hooked status and explicit fallback. Remove misleading claims.

**Acceptance, automated and manual/visual tests:** Portable routing policy tests; Windows source registration/capability tests; primary-source architecture record.

**Performance:** Use existing OBS plugin, no extra runtime/driver/injection.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## C2 — Actual source selection and capture

**Objective/user benefit and expected behavior:** Explicit opt-in process audio configuration, start IPC carries exact identities; create and hook real process sources, clean up on stop/failure; no silently substituted system audio when isolation requested. Mic remains opt-in.

**Dependencies:** B1,C1.

**Exact scope/likely files:** native main.cpp and audio routing policy, CaptureHostClient/Protocol, SettingsPage, audio mix rules, media exporter/tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Explicit opt-in process audio configuration, start IPC carries exact identities; create and hook real process sources, clean up on stop/failure; no silently substituted system audio when isolation requested. Mic remains opt-in.

**Acceptance, automated and manual/visual tests:** Window/process exit/restart, unsupported source/build, hook failure, mic off/on, source lifetime; real tone capture where accessible.

**Performance:** Bounded sources/encoders; measure process loopback cost on PC.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## C3 — Honest editor and export mix

**Objective/user benefit and expected behavior:** Label actual recorded tracks, default to isolated tracks or combined fallback without duplication; independent mute/gain/default reset, explain original vs combined/export preview; real export mixing.

**Dependencies:** C2.

**Exact scope/likely files:** native main.cpp and audio routing policy, CaptureHostClient/Protocol, SettingsPage, audio mix rules, media exporter/tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Label actual recorded tracks, default to isolated tracks or combined fallback without duplication; independent mute/gain/default reset, explain original vs combined/export preview; real export mixing.

**Acceptance, automated and manual/visual tests:** Independent synthetic tones verify channel contribution/mute/gain/default/no double mix, full MP4 decode/sync.

**Performance:** Reuse FFmpeg; avoid parallel playback decoders.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.

## C4 — Audio isolation acceptance

**Objective/user benefit and expected behavior:** Collect independently audible game/voice/mic signals and device changes; report attachment vs signal, input/output changes, headphones/speakers and overhead.

**Dependencies:** C2,C3.

**Exact scope/likely files:** native main.cpp and audio routing policy, CaptureHostClient/Protocol, SettingsPage, audio mix rules, media exporter/tests. Limit edits to this slice; root integrates shared files.

**Implementation:** Extend the existing architecture and fail closed on invalid state; preserve saved settings, original files and pending saves. Collect independently audible game/voice/mic signals and device changes; report attachment vs signal, input/output changes, headphones/speakers and overhead.

**Acceptance, automated and manual/visual tests:** Windows physical endpoints/Discord process and tone evidence plus reproducible scripts; hosted synthetic tones only verify mixing, not capture isolation.

**Performance:** No hardware isolation claim until actual capture evidence exists.

**Completion evidence:** Source commit + reviewed diff + exact test run/log counts + applicable render/interaction receipts in evidence ledger; check the tracker only after these pass.

**Known limitations:** Physical GPU, real audio endpoint and Discord desktop checks require an accessible authorized Windows PC. Mark individual unverified gates separately from implemented or CI-tested behavior.
