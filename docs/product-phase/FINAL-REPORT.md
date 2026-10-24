# Flick product phase acceptance

Implementation is merged through [PR49](https://github.com/Krish-Verma/Flick/pull/49) at `70e6076eba1fb0e475072b2d240c7bf80fcb9a18`. Available automated, media, visual and installer checks pass on the identical source tree. Physical capture, audio isolation, gameplay performance, Discord and signing remain open. Candidate `1.0.7-preview.1` is unsigned and unreleased.

Flick needed opt-in game replay, actual app-specific audio choices, faster local editing and an obvious way to transfer the validated MP4. This integrates those workflows with recording health, preserves the purple/graphite interface, and keeps originals, settings and published previews intact.

## Implemented scope

| Workstream / slices | Result | Remaining acceptance |
| --- | --- | --- |
| A1 health; A2 private logs; A3 consented self-test; A4 recovery harness | Already integrated through PR44–48; correlated saves, retained failures, allowlisted redacted logs and a separate finite synthetic-window harness | Seven physical capture/recovery cases, real hotkeys and endurance |
| B1 discovery; B2 profiles; B3 lifecycle; B4 qualification | Explicit approved executable profiles, disabled-by-default scanning, serialized starts/stops, exact HWND/path/PID/birth binding, persistent manual override; qualification tools and deterministic failure tests | Real game launch/close/minimize/restart/anti-cheat and measured capture overhead |
| C1 feasibility; C2 native routing; C3 editing/export; C4 isolation qualification | Exact Windows process-loopback sources, genuine audio-role provenance, opt-in microphone, independent mute/gain/defaults, complete-mix alternative that avoids double mixing; generated-process tone harness | Actual independent source isolation, device/Discord changes, microphone/endpoints and overhead |
| D1 organization; D2 timeline; D3 bounded frames; D4 export | Favorites, tags, game/date/source filters, 20k-row query regression, markers, nondestructive trim/undo and saved states, six optional frames, recent valid exports, fitted 9:16/1:1 | Physical playback/scrubbing and equivalent gameplay/process-memory comparisons |
| E1 research; E2 tile; E3 file transfer; E4 QA | Brighter Share panel, actual bounded still, title/duration/size/readiness, whole-tile native file drag, Unicode CF_HDROP clipboard fallback, stale/offline/cancel/failure states and keyboard/accessibility work | Actual Discord desktop attachment, real display scaling/High Contrast interaction |

B/C/D complete feature refs are preserved with identical source trees; root resolved shared schema, target capabilities and worker lifetime conflicts. See [parallel integration record](PARALLEL-INTEGRATION.md), [master plan](MASTER-PLAN.md), [evidence ledger](EVIDENCE.md) and [user guide](../USER-GUIDE.md).

## Architecture and repairs

Retain the WPF/native-libobs/local-JSON/FFmpeg architecture. The save toast remains only Clip Saved plus actual duration. Strict game windows and audio processes retain full executable/process-instance identities and refuse ambiguity or unrelated fallback. Capture, diagnostics, audio previews, thumbnails, filmstrip, exports and quit participate in owned cancellation/settlement gates; requesting cancellation alone does not release a worker.

Integration review repaired stale Share callbacks, same-path thumbnails, source-image/network bounds, start/stop races, incompatible auto-game/audio identities, recent-export cap restoration and original-thumbnail/fit explanations. Later pixel review repaired the tiny minimum Share viewport, crowded library details, stale unsaved captions and offline Export enablement. Library scrolling preserves the virtualized list; fixed actions remain reachable. Games explicitly labels current live status beside staged preferences.

The renderer's earlier stale get-only bindings were a fixture queue defect: bounded real ContextIdle pumping now lets WPF process background notifications. Strict current-value assertions remain; no UpdateTarget, rebinding or substituted pixels.

## Exact source automated acceptance

Candidate head: `38ab994f29ffbb1dafd53cbf6a44029dfeb4adbb`; tree: `65de858c27eeb4619ad295ebdb09a29d042fdaa9`. Local integration commit `c138e983bd0fc6c277480eaed9f7bff28c509d94` has the same tree. Renderer test-merge `1d9d574f4e02f6b8dcfa8e185b6df87b5c6db2bf` was independently verified against it.

- [Windows CI40102045722](https://github.com/Krish-Verma/Flick/actions/runs/40102045722): Core467 passed/0 failed/17 explicit hardware-media skips; App9 passed/0 skipped, including real clipboard data, bounded images, dispatcher queues, edit persistence, cancellation ownership and stale/offline state. Native/policy/source gates,20 non-capture harness checks and publish build pass.
- Same run: all8 generated-media cases execute with no skips. Real trim/mixing/contribution/mute/gain, no doubled complete mix, fitted aspect geometry, audio/decode/size/cancellation and original hashes are exercised. Generated media proves mixing/encoding, not process capture isolation.
- [Actual WPF renderer40102045791](https://github.com/Krish-Verma/Flick/actions/runs/40102045791): all67 numbered states plus scale samples pass. Artifact12562823760 SHA256 `3742228a0e37de8d9b3d1c3a7924801a82e0e88edd52d08dd54dda5bedf1fbc3`. At720×480, expanded details retain108DIP list,156DIP details and95DIP header viewports; Save and all categories are reachable. All16 strict Share observations match the current clip; Ready uses genuine production-exported1,557,658-byte/3.021s MP4.
- Package acceptance: [Installer/package40102045726](https://github.com/Krish-Verma/Flick/actions/runs/40102045726): pinned Setup/ZIP verification and all four product/numeric PE version assertions passed (`1.0.7-preview.1` / `1.0.7.0`); real1.0.4→1.0.7 install/update wait/restart and sentinel retention passed with no cleanup errors. Small lifecycle artifact12563133443 SHA256 `cb0a52ec0eb0349bf9bffcbd0b0adbaefa997b6a3bbead0873dcc1d57cebbe33` was independently verified. Setup SHA256 `669B43BFA683961467F7BDA7AC8CF5C61FA66F8D67622A95CA7C9412CDFA5333`; portable ZIP `679BCB18F729AB6CD0DA2CC275EAA3C4CFF13476AC36A1C6C4405509AEF099F6`. Separate recording/audio qualification payloads assembled; assembly is not capture execution, and their large embedded inventories were not independently downloaded in this final review.

Baseline and redesigned Share screenshots are the actual1240×720 WPF unexported fixture; later Ready views use genuine generated media. 100/125/150/200% raster samples and forced local accessible palette are distinguished from actual OS DPI/High Contrast operation. Original/profile hashes remain unchanged.

## Performance and qualification limits

The final Windows six-frame test passed in1.3144518s:345,600 retained decoded pixel bytes (337.5KiB); managed memory4,136,024→6,215,232bytes, a2,079,208-byte snapshot delta including concurrent runtime allocations. This is six generated320×180JPEGs retained as frozen160×90images. It is not peak process/native memory or an isolated before/after app benchmark. The20k-row query regression passed; eight real media cases passed in17s aggregate. Equivalent gameplay CPU/GPU/FPS/1% lows, startup/save latency, device overhead and2h/8h endurance remain unmeasured because no authorized interactive capture session is available.

Five distinct passes covered completeness, state/lifetime/media correctness, bounded work and measurements, actual-pixel UX, and source/version/install/release readiness. Source bounds are safeguards rather than FPS or leak-free evidence. No universal NVIDIA/AMD/Intel, audio-isolation, Discord, signing or stable-quality claim.

The owner PC is accessible through SSH in Session0. The same-owner desktop bridge failed before child creation with Windows1314; latest admission found only894MiB available. No privileges/security/drivers, owner processes, installed Flick or real profile were changed. Prepared recording/audio payloads require an owner-launched desktop session and fresh resource admission. Physical acceptance remains with the integration lead once that session is available.

## Distribution

Unique candidate `1.0.7-preview.1` / numeric `1.0.7.0`; unsigned and unreleased. Product code remains GPL-2.0-or-later with separate bundled dependency obligations. Licensing redistribution review, SignPath/Store, physical/endurance qualification and fresh explicit publication approval remain release gates. Public Latest remains `v1.0.6-preview.1`; no release/tag/assets are overwritten.

Tracks #36, #38, #39, #40, #41, #42. These issues remain open for the explicitly unverified acceptance gates.


## Screenshots and records

The [final WPF artifact](https://github.com/Krish-Verma/Flick/actions/runs/40102045791) contains the comparable redesigned `04-share.png`, genuine Ready `37-share-ready-1080.png`, minimum `40-share-ready-minimum.png`, and details/navigation `65`–`67`. The [baseline artifact](https://github.com/Krish-Verma/Flick/actions/runs/40084437461) contains the earlier `04-share.png` at the same1240×720 viewport. Both comparison images use an unexported synthetic clip; the later Ready fixture uses actual generated/decoded media. The retained local receipts include all extracted images, hashes, raw TRX and installer results. No gameplay image is presented as live capture.

The main roadmap, five workstream plans, evidence ledger, implementation records, user guide and1.0.7 readiness checklist are updated. Issues36,38–42 retain only explicit qualification gates. Feature branches and their exact equivalent trees remain listed in the parallel integration record; no historical releases or refs were removed.
