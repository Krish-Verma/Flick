# Flick GUI Refresh R2 — Native dark chrome and themed navigation surfaces

**Branch:** feat/gui-refresh-r2-themed-chrome-menus-20261023. **Base:** R1 PR #16 at exact head c417e6714cecf0a8cb6ea2c0b9edb77c08b5b3b4. R2 is a stacked source-only UI branch, not a production build or published release.

## Implementation

1. **Native title bar:** Use DWM on the actual Windows 11 HWND (immersive dark mode, graphite caption/border and bright title text). The existing Windows minimize, maximize, Win+Arrow, Snap Layouts, resize and drag handling remain OS-owned. No custom borderless chrome, new media player, timer, visual animation or background process.
2. **Brand:** Keep the actual embedded 2.3 KB original FlickLogo.png, center it and apply a 38-DIP circular clipping geometry. Match Settings/main navigation/toast sizes, avoid squircle edges showing outside the circle and avoid stretching the picture.
3. **Scrollbars:** One application-level lightweight resource dictionary provides 11-DIP dark WPF rails, 25-DIP minimum thumb along scroll travel axis, muted idle color and lavender hover/drag. Native WPF Track/RepeatButton page scrolling, wheel, keyboard and virtualized ListBox behavior remain. No third-party visual toolkit or per-frame polling.
4. **Context menus:** Popup menus such as Original / Export Preview / Open in External Player and clip ellipsis use shared graphite/lavender WPF ContextMenu and MenuItem templates, mouse-hover and keyboard-focus feedback. Existing click/keyboard handlers remain untouched. The WinForms tray context menu is Windows-owned and is intentionally not emulated in WPF.
5. **Shared tokens and CI:** App.xaml loads Flick.Chrome.xaml once. GUI design token parser and Slice 6 XAML checks include it. scripts/check_gui_refresh_r2.ps1 is invoked in Windows CI and requires native caption, one circle-clipped logo, themed scroll/menu resources and preserved virtualization.
6. **Separate pending plans:** R3 video-first layout, R4 context edit drawers, R5 share, R6 premium shortcut binding, R7 themed app-owned confirmations/performance, and R8 Clip Saved with actual-duration subtitle and bounded customizable sound remain separate reviewable slices.

## Risks and follow-ups

- Title bar DWM attributes are best-effort on supported Windows versions. High-contrast Windows caption must remain in control; fallback may use OS defaults. Physical screenshots are needed to confirm no white title strip.
- Theme scroll/menu XAML compilation is verified by Windows CI, but actual mouse wheel, page buttons, key focus, submenu, snapping and DPI behavior require physical Windows acceptance.
- Do not claim the currently unsigned installer is Windows Smart App Control trusted. R2 does not alter GPL licensing, signing/release publishing, capture host/NVENC or recorded files.
- Use the R2 AntiGravity test-only handoff for real screenshots, RAM/handles and FPS baselines against the existing preview (same fixtures/settings).

Success means native WPF UI styling is code-complete and CI-green, but not full Windows visual/hardware acceptance.
