# Flick desktop skin refinement

## Requested scope

The owner likes Flick's current architecture and workflows, but finds the skin and icons too Android-like. This pass changes the visual layer toward the restraint and precision of Apple's dark desktop pro apps. It preserves the native Windows title bar, navigation rail, library/player arrangement, four editing actions, grouped settings, capture behavior, non-destructive edits, sharing consent and update flow.

The original Flick artwork and its optical alignment remain. The sunset spectrum belongs to that artwork; a muted violet is reserved for primary actions and keyboard focus. No Apple-owned font or SF Symbols assets are redistributed.

## Visual decisions

- Neutral charcoal canvas, sidebar and raised controls replace the violet-tinted surface stack. Opaque surfaces give a stable fallback without a new blur/effect pipeline.
- Regular 13-DIP control text, clear section weights and tabular transport numerals reduce the previous blanket semibold treatment. Windows' installed Segoe UI Variable remains the native typeface.
- Authored vector symbols use a consistent rounded 1.5-DIP stroke. Share uses an arrow rising from a square. Play, pause, back, refresh, more and dropdown indicators no longer depend on Unicode icon substitutes.
- Compact six-DIP control corners, flatter grouping, neutral selected rows and a contiguous source segment reduce outlined capsules. Editing actions retain their evenly spaced four-column arrangement, visible labels and 52-DIP hit targets.
- Settings retain the same categories and fields with a readable content width, aligned insets and fewer nested borders. Trim grips and transport chrome are neutral; the active focus indicator stays visible.
- The real Windows caption reads colors from the same named palette. Standard resize, Snap and window controls remain native.
- Dropdown selections stay on one line with an ellipsis at compact widths. Advanced sections use the same small disclosure chevron, with hover, keyboard focus and two-way expansion behavior preserved.

## Impeccable guidance

Applied the official Impeccable 4.7.0 skill package at upstream commit `5cb95eea9c96479f57afe0732a45470d30183326`, principally `quieter`, Operate-mode guidance and the craft floor. The context command ran once. This is a scoped refinement of existing native WPF code with the layout and aesthetic direction already specified by the owner; no web framework, global plugin installation or browser-injection hook was introduced.

The Impeccable web detector cannot establish native WPF rendering correctness. Actual Windows WPF renders and the existing source/accessibility contracts are the visual and structural evidence. The renderer uses synthetic clips and does not show gameplay, start a recording host or request an update.

## Verification

The five existing text/background pairs measure 16.75:1, 15.61:1, 9.11:1, 5.44:1 and 7.36:1 against the final palette. Keyboard focus, disabled/busy behavior, native slider tracks and recycled list virtualization remain in the control templates.

Render review covers the default workspace, Trim/Audio/Share, settings, save banner, shared/native logo, a 1440×900 workspace and the existing 800×600 compact library/editor modes. Physical Windows title-bar behavior, per-monitor DPI, gameplay and GPU performance remain separate acceptance checks.
