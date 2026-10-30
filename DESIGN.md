# Flick design system

## Authority and mode

Operate mode. The owner's established purple/graphite identity and supplied corrected Flick logo remain the visual authority. The October 30 research report refines this as Graphite Studio: a quiet native media utility, clear next actions and honest state. See `docs/DESIGN-RESEARCH-IMPLEMENTATION-20261030.md` for evidence and traceability.

## Composition

One native WPF window; persistent capture bar and 60-DIP destination rail; library and video workspace split above 1120 DIP, one primary pane below. Minimum 720x480 DIP. Trim, Audio, Share and Health are contextual. Settings has seven categories, one selected-content scroll region and a fixed Save/Cancel footer. Share must fit without nested scrolling.

## Visual grammar

Graphite canvas/surface/raised layers, lavender primary action, semantic colors with text for actual status. Preserve the sunset mark as a localized brand accent. No decorative blur trees, repeated glow, looping gradient or per-row effects. Segoe UI Variable/system fallback; page 20-22, clip title18, sections15-16, body14, primary controls14, dense labels13, secondary metadata12. Use existing 4/8/12/16/24/32 spacing,6-DIP controls,8-DIP panels and approximately10-DIP transient corners.

Every interactive control has readable rest/hover/press/focus/selected/disabled/pending states. Focus must be distinct without changing layout. Use existing bounded event-driven hover, reduced-motion immediate feedback and system High Contrast overrides. Labels use sentence case; user-authored titles retain their case. Time/size/counter fields use consistent tabular numerals.

## Product constraints

Truthful pending versus verified success/failure; only actual retained buffer history. Save banner stays only Clip Saved plus actual duration. Originals are non-destructive, microphone opt-in, sources explicitly approved, network sharing explicit. Copy file/Open folder remain alternatives to drag. Preserve virtualization/recycling, deferred video, bounded still decoding, cancellation and no extra permanent idle loop. Measure performance claims on matched inputs; keep physical hardware acceptance distinct from synthetic renders/tests.
