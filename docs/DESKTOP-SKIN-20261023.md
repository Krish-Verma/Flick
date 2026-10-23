# Flick desktop skin refinement

## Requested scope

The owner likes Flick's current architecture, workflows and purple theme. This pass refines the bottom toolbar icons toward the precision of Apple's desktop apps and reduces the visual noise of dividing lines. It preserves the native Windows title bar, navigation rail, library/player arrangement, four editing actions, grouped settings, capture behavior, non-destructive edits, sharing consent and update flow.

The original Flick artwork, optical alignment, violet-tinted surfaces and bright lavender accents remain. No Apple-owned font or SF Symbols assets are redistributed.

The logo's gradient identity appears in small functional details: the selected Clips navigation symbol, the active/hovered/focused editing symbol and a restrained violet gradient on primary actions. Large surfaces and dividing lines stay quiet. Replay state colors keep their existing meanings.

## Visual decisions

- Preserve the original purple canvas, sidebar, raised controls, translucent surface colors and accent palette. A separate subtle divider color blends structural boundaries into the adjacent surfaces without weakening keyboard focus.
- Regular 13-DIP control text, clear section weights and tabular transport numerals reduce the previous blanket semibold treatment. Windows' installed Segoe UI Variable remains the native typeface.
- Authored vector symbols use a consistent rounded 1.5-DIP stroke, with balanced 20-DIP toolbar boxes. Share uses an arrow rising from a square. Play, pause, back, refresh, more and dropdown indicators no longer depend on Unicode icon substitutes.
- The small symbol gradient uses the logo's sunset and coral colors with a bright lavender endpoint for legibility. Primary-action gradients stay within a narrow pink-violet/lavender range. Windows high-contrast mode replaces these gradients and the new divider brush with system colors.
- Compact six-DIP control corners, flatter grouping, violet selected rows and a contiguous source segment reduce outlined capsules. Editing actions retain their evenly spaced four-column arrangement, visible labels and 52-DIP hit targets.
- Settings retain the same categories and fields with a readable content width, aligned insets and fewer nested borders. Trim grips and transport chrome are neutral; the active focus indicator stays visible.
- The rail, header, library, preview and footer boundaries use the softer divider shade. The four-DIP library resize target is transparent until hovered or keyboard-focused. The real Windows caption reads colors from the same named palette. Standard resize, Snap and window controls remain native.
- Dropdown selections stay on one line with an ellipsis at compact widths. Advanced sections use the same small disclosure chevron, with hover, keyboard focus and two-way expansion behavior preserved.

## Impeccable guidance

Applied the official Impeccable 4.7.0 skill package at upstream commit `5cb95eea9c96479f57afe0732a45470d30183326`, principally `quieter`, Operate-mode guidance and the craft floor. The context command ran once. This is a scoped refinement of existing native WPF code with the layout and aesthetic direction already specified by the owner; no web framework, global plugin installation or browser-injection hook was introduced.

The Impeccable web detector cannot establish native WPF rendering correctness. Actual Windows WPF renders and the existing source/accessibility contracts are the visual and structural evidence. The renderer uses synthetic clips and does not show gameplay, start a recording host or request an update.

## Verification

The five existing text/background pairs measure 17.24:1, 15.81:1, 8.88:1, 5.39:1 and 7.82:1 against the final palette. Keyboard focus, disabled/busy behavior, native slider tracks and recycled list virtualization remain in the control templates. The Windows staging test exercises the real disclosure template's two-way expansion and content visibility.

Render review covers the default workspace, Trim/Audio/Share, settings, save banner, shared/native logo, a 1440×900 workspace and the existing 800×600 compact library/editor modes. Physical Windows title-bar behavior, per-monitor DPI, gameplay and GPU performance remain separate acceptance checks.
