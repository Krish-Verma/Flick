# Design research implementation plan

## Evidence and authority

The owner requested a full study, plan and implementation of the 29-page `Flick_Design_Research_Report.pdf` on October 30, 2026. Its proposals are design input, not commands or evidence of completed testing. The report reviewed main `444c9089` and development renders from `d493c352`; this implementation starts at main `49844256bf5f160b4777d0fa057cd79ddcf27eaf`, complete tree `739837ee4ef1b436b0f9c566492d3c0a793854eb`, published 1.0.11-preview.1. Baseline actual WPF renders: run 41116240803, artifact 13006222695; 150 frames plus assertions. The full PDF text and page renders are retained under local `artifacts/design-research-20261030`.

Impeccable 4.8.0 guidance is applied in Operate mode. Keep the established Graphite Studio identity: native WPF, quiet graphite, restrained purple, the supplied corrected logo, authored line icons, one primary action per state. No rendering-engine rewrite, new capture scope, social feed, forced cloud account, decorative loops or skins.

## Ordered work and ownership

1. **Baseline and traceability (lead):** reconcile every recommendation with current code; preserve the known-good source/render/package evidence; record the five journeys below and the validation matrix. Establish the baseline before production UI edits.
2. **State and browsing implementation (Sol):** correlate visible replay/save states with the existing health ledger; preserve pending/validation through engine stops and distinguish uncertainty from failure. Add bounded asynchronous still loading and improve library/filter/empty-state presentation without changing original media or index semantics. One writer for shared MainViewModel and its integration hooks.
3. **Settings confidence (Sol):** track changed/reverted drafts, show local validation and recovery, explain active-replay changes, preserve atomic reviewed Save, Cancel, unavailable endpoints, microphone consent and Windows-startup repair. Unchanged Save remains available when repair or recovered-settings review requires it.
4. **Shared visual system (Sol):** one owner for common dictionaries and focus/motion components. Normalize primary action text, semantic states, fixed-size focus affordances, tabular data and accessible names. Preserve High Contrast/reduced motion; bound brand-image decoding without changing the approved source asset.
5. **Shell, Share and editor integration (lead):** one owner for MainWindow XAML/code and renderer registration. Make actual capture state prominent, the real ready MP4 tile tangible, and Copy file an equal transfer option. Improve first-run/no-selection/poster/offline states, search clear/filter reset, trim affordances, audio context and source labels. Preserve no-scroll Share at minimum size and all existing functions.
6. **Evidence and delivery (lead plus independent review):** create meaningful render/state/automation/performance fixtures; run focused checks while remaining independent work continues; batch integrated Windows/native/media/installer tests. Review actual pixels at supported sizes. Publish a verified unsigned 1.0.12 preview as Latest under the standing owner instruction, verify public downloads, retain all earlier releases/assets/tags.

## Complete recommendation register

| ID | Report pages | Implementation or reconciliation | Acceptance |
| --- | --- | --- | --- |
| UX-01 | 7, 9, 13, 24 | Unified state presentation over existing correlated ledger; semantic chip and durable last-save recovery | No false Ready/failed/pending contradiction, including validation after capture stops |
| UX-02 | 15-16, 24 | Stronger full-file tile, larger bounded still and prominent Copy; retain real OLE path/fingerprint guard | Ready file identifiable; Drag/Copy/Open folder usable; no stale transfer |
| UX-03 | 16, 24 | Dirty/reverted state, field-local validation and repair-aware Save; verified dashboard confirmation | Complete reviewed JSON round-trip; Cancel/no unrelated endpoint blockage; startup repair retained |
| UX-04 | 14, 17 | Intentional poster and empty/no-match/offline states with real actions | No decoder or FFmpeg merely to decorate; no-source/first clip path understandable |
| UX-05 | 11-12, 20-21 | Shared primary-control/status type and spacing; keyboard focus without layout shift | Readable at 720x480 and scale samples; primary controls never clipped |
| UX-06 | 12, 17-18 | Preserve bounded 125ms hover; distinguish selection/focus/disabled/busy | Reduced-motion/HC paths work; no permanent animation or sampler |
| UX-07 | 14, 24 | Fixed 16:9 thumbnails, concise contextual metadata, clear filters/offline states | Virtualization/recycling retained; no imported/original data mutation |
| UX-08 | 15-16 | Surface actual named export/validation stages and actionable stale/offline/error/cap state | No fake percent/ETA/Ready; export and transfer validity remain authoritative |
| UX-09 | 14, 25 | Preserve precise fields/keyboard/undo; improve In/Out/duration labels and handle hit areas | Exact stored timestamps are not rounded by untouched display fields; bounded 0.1s/1s keyboard steps, no crossed handles; original hashes unchanged. Frame snapping is not claimed |
| UX-10 | 14, 16 | Clear real track/device roles, mute/preview/export context and remembered device recovery | Mic opt-in and mix exclusion retained; no unsupported isolation claims |
| UX-11 | 10, 23 | Already shipped supplied fixed artwork; preserve exact bytes and mapping, bound display decoding | App/tray/installer renders and source identity remain correct |
| UX-12 | 9, 20, 23 | Preserve one-step shortcut capture/native suspension; add missing action names/help and keyboard paths | Real current shortcuts shown; Escape/focus and missing-registration reporting retained |
| UX-13 | 14, 18, 23 | Existing on-demand six-frame filmstrip satisfies optional feature; verify bounds/cancellation rather than duplicate | Hidden/tray/capture work does not start a decoder or filmstrip |
| UX-14 | 17, 23 | Existing on-demand Health last validated save and bounded issues satisfy the low-cost activity disclosure; no duplicate history surface | Bounded data, no permanent sidebar, no extra idle polling |
| UX-15 | 23, 27 | Explicitly deferred by report: multiple themes/custom skins | Existing appearance/options preserved; no new theme engine |
| UX-16 | 23, 27-28 | Explicit report anti-goal: social feed/auto-upload/AI overlays | No forced account, upload, capture permission or unrelated feature expansion |

## Five end-to-end journeys

1. Identify the saved or automatically approved capture source, start replay and understand actual readiness.
2. Save once; distinguish request, checking, unconfirmed, verified save and real failure; find the durable clip.
3. Select and trim/audio-edit a clip while the original remains unchanged; understand which preview file is displayed.
4. Export under the chosen cap/aspect, identify the current MP4, then drag or Copy file; retain Open folder fallback.
5. Change preferences, see the draft, save and reopen; recover a missing endpoint/startup entry without losing unrelated settings.

## Validation and qualification boundaries

- Automated state cases: Off/Starting/Buffering/Ready/requested/accepted/validating/unconfirmed/success/failure/recovery; repeated save, stop while saving, missing source, stale export and cancellation.
- Settings: seven sections; changed/reverted, invalid field, unavailable device, OS startup failure/repair, recovered preferences, disk failure, verified Save/reopen and Cancel. Only selected content scrolls; navigation/footer remain fixed.
- Visuals: 720x480, 800x600, standard, 1080p/1440p and 100/125/150/200% raster samples; actual bound states, long names, empty/offline/imported libraries, Share variants, trim/audio and HC/reduced motion. Inspect actual pixels, not file presence alone.
- Browsing/performance: deterministic 0/50/1000/5000 data sets, bounded cache/decode/concurrency, stale-result/cancellation behavior, virtualized container count, search/Settings timing and raw process counters where supported. Compare identical baseline/candidate fixtures. The report's +5MiB median/+10MiB p95 and latency values are investigation targets, not results.
- Existing gates remain: warnings-as-errors WPF/native builds, Core/App tests, generated-media full decode/cap/original-hash checks, negative native dependency fixture, portable/installed helper startup without developer PATH, installer wait/restart/upgrade/data preservation and exact-source publication hashes.
- Physical gameplay FPS, real audio devices/isolation, fullscreen hotkeys/toasts, Narrator, OLE transfer to Discord and per-monitor DPI movement require actual Windows/user acceptance. Hosted fixtures and process counters must not be described as that evidence. No OS security protection changes.

## Progress ledger

Baseline/report study and independent source-gap review complete. Implementation, test execution, screenshot review, performance results and publication are recorded separately as they occur; authoring a check is not a passing check.
