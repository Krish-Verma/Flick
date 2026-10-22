# Flick — GUI Design Foundation v3.2 (Slice 1)

**Branch:** `feat/gui-slice1-design-foundation-20261020`  
**Review PR:** https://github.com/Krish-Verma/Flick/pull/2  
**Backend base:** `07e732f423a343a36ee01574cfea1e2b512602b8` from draft PR #1  
**Status:** code committed for Slice 1 only; visual review and Windows qualification pending.

## Contract

Flick remains a fast, local, no-account Windows gameplay clipper. The goal is a polished, readable and visually restrained WPF GUI, not a web frontend or resource-heavy glass simulation. The backend (native libobs, FFmpeg, media storage, IPC, capture/save/export states) is not modified by this slice.

**Do not overwrite the existing normal main-window styles in Slice 1.** New styles have explicit resource keys and are loaded through `App.xaml` for future migration of the shell, gallery, player, settings and export dock in separately approved slices.

### Colors

- Canvas `#0B0B14`, Surface `#151622`, Raised `#242436`.
- Text Primary `#F2EFF9`, Secondary `#B9B3CC`, Muted `#9C97AD`.
- Interaction Accent `#C69BFF`, Accent Hover `#D9BCFF`.
- Logo spectrum, used sparingly: Sunset `#EA8B65`, Coral `#E25986`, Magenta `#DC3CB4`, Violet `#9A27E2`, Indigo `#4B1FDE`.
- Semantic colors are reserved for real statuses: success, warning, error.
- No full-card gradients, neon glow, animated backgrounds or ongoing refraction.

### Typography

Segoe UI Variable Text with Segoe UI fallback. Only six base values:

| Role | Size | Line Height | Weight |
| --- | ---: | ---: | --- |
| Page heading | 20 | 28 | Semibold |
| Clip title | 18 | 24 | Semibold |
| Section heading | 16 | 24 | Semibold |
| Body | 14 | 20 | Regular |
| Control | 13 | 18 | Semibold |
| Caption | 12 | 16 | Regular |

Design-authored headings and action labels use Title Case. User-created clip names must never be silently re-capitalized.

### Geometry and controls

- Base spacing is 4/8/12/16/24/32 device-independent pixels.
- Standard controls 36 px minimum height; primary buttons 40 px; icon buttons 36×36 px.
- Controls 8 px radius, panels and floating chrome 12 px, logo 10 px.
- Tokens and reusable styles are in `Resources/Theme`. No external font assets, icon libraries, web view, telemetry or expensive animation framework.
- Keyed button styles: Primary, Secondary, Ghost, Icon, Danger, Busy. Supported states: hover, pressed, keyboard focus, disabled.
- TextBox, ComboBox (including popup items), Toggle, Slider, progress, typography and panel styles are reusable.
- Revalidate real keyboard behavior, focus order, contrast, high contrast, reduced motion and common DPI scales in Windows.

### Brand mark and Clip Saved chrome

- `Assets/Brand/FlickLogo.png` is a small 128×128 crop of **the entire original logo squircle from the user-supplied reference image**. It removes the reference image's “03 Retro Circle” sample caption.
- `Views/Components/FlickClipToast` displays that bitmap in a 38×38 square area with **no inner circle, additional logo badge or full-width gradient bar**. The artwork itself fills the squircle.
- The notification chrome is roughly 278 px wide and 64 px high, with a subtle graphite translucency, white “Clip Saved”, actual clip details and one restrained success check.
- **Displaying a non-activating in-game overlay, notification duration, success sound, timing, toast queue and independent toggle settings are Slice 2**, not functional in this slice. Do not claim they work yet.

### Glass

`Services/FlickBackdropService.cs` contains a small opt-in Windows 11 `DwmSetWindowAttribute` helper using `DWMWA_SYSTEMBACKDROP_TYPE`. It never starts a capture loop, process or graphics shader. Calling it successfully does **not** prove that frosted wallpaper translucency is visible in all WPF windows. The review gallery is readable even when Windows declines the backdrop; production use waits for real DPI/GPU comparison tests. High-contrast mode disables attempted Mica.

## Preview

Build on Windows and launch the design-system window with Flick closed:

```powershell
dotnet run --project src/LocalClip.App/LocalClip.App.csproj -- --design-preview
```

The normal launch path still opens the original app. Design-preview mode does not instantiate `MainViewModel` or start capture. Use Tab/Shift+Tab/Space and mouse to review button, input, toggle and slider styles; check the real logo is the **entire squircle** and that no nested circle appears. All sample controls are previews, not connected to recording/exports.

## Checks and approval

```powershell
powershell -ExecutionPolicy Bypass -File scripts/check_gui_design.ps1
dotnet build LocalClip.slnx -c Release --warnaserror
powershell -ExecutionPolicy Bypass -File scripts/run_tests.ps1
```

CI verifies XAML compilation, resource keys, clean app publish, offline tests and native compilation. It **cannot verify WPF appearance**, compositor translucency, contrast on a display, keyboard focus in the actual gallery, DPI overlap or game FPS on the owner's NVIDIA PC. Those remain an explicit AntiGravity screenshot/performance handoff.

### Acceptance gates before Slice 2

1. Real WPF gallery screenshots on Windows at 100%, 125%, 150%, 200% DPI; 760 px minimum width, 1000 px normal width. No clipped labels, overlapping sample controls, inconsistent height/radius or clipped popup menus.
2. Actual logo artwork fills the toast icon area with no smaller circle; notification is readable without adding decoration or continuous processing.
3. Test all control states, keyboard focus order, ComboBox opening/selection/closing, Slider pointer/keyboard changes, toggles and Input typing with Windows high contrast and reduced-motion options.
4. Compare process RAM after standard app idle and after design preview with/without Mica, noting system caching; gallery should create no capture-host or FFmpeg process.
5. Existing Windows runtime clipping/Replay/MKV/export/hotkey behavior must not regress because of new merged resources or the development-only preview switch.
6. No merge, release, public visibility change or Slice 2 implementation without owner review and explicit authorization.

## Deferred slices

Slice 2: replace shell and implement real temporary Clip Saved overlay/sound, independently toggleable with defaults On.  
Slice 3: compact clip library, external folder linking/import, independent original/export storage.  
Slice 4: player, non-destructive trim range and audio mixer.  
Slice 5: sticky Discord drag/copy/export and reorganized Settings.  
Slice 6: full visual QA, gaming performance and release acceptance.
