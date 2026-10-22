# Flick — Slice 1 WPF Design Validation Handoff

**Branch:** `feat/gui-slice1-design-foundation-20261020`  
**PR:** https://github.com/Krish-Verma/Flick/pull/2 (draft; targeting backend PR #1 branch)  
**Approval boundary:** Slice 1 design foundation only. **Do not start Slice 2.**

1. Read `docs/GUI-DESIGN-FOUNDATION-V3.2.md`, every theme XAML file under `src/LocalClip.App/Resources/Theme`, `App.xaml`, `App.xaml.cs`, and the preview/toast XAML and codebehind. Confirm it builds on the current backend without changing `MainWindow.xaml` or capture code.
2. Run `scripts/check_gui_design.ps1`, `dotnet build LocalClip.slnx -c Release --warnaserror`, and `scripts/run_tests.ps1`. Verify `--design-preview` launches only the design gallery with the full logo and no capture host. Verify normal launch still opens the legacy functional clipper, recording disarmed.
3. Capture actual Windows screenshots of the gallery at 760×550 and ~1000×760 DIPs, with DPI scaling 100%, 125%, 150%, and 200%. Check alignment, font weights, line height, display text clipping, overflow, margin consistency, buttons/ComboBox popup placement, Slider track and thumb, toggle states, and the preview toast.
4. Test Tab/Shift+Tab/Space, screen reader names where applicable, high contrast and reduced motion. If custom ComboBox/Slider templates break native keyboard or pointer behavior, fix them before marking the design system complete.
5. Verify the branded Clip Saved chrome is about 278×64 px and contains the actual full squircle logo occupying its 38×38 icon slot, no inner circular badge and no decorative separator bar. This is static chrome; do NOT report an in-game overlay as implemented.
6. Measure memory and CPU for design preview with/without attempted Mica and ensure no new helper process or continuous rendering loop. Do not represent a DWM successful HRESULT as proof of visible backdrop, and do not claim NVIDIA gameplay FPS until tested in Slice 2.
7. Log screenshot paths, exact test command outputs, warnings/defects, fix commits, CI run URLs and PASS/FAIL/BLOCKED per gate. Keep docs accurate; do not merge or publish without approval.

This handoff includes no live Discord post, game injection or new publishing permission.
