# Shared logo alignment and actual WPF renders

## Approved artwork, 2026-10-30

`src/LocalClip.App/Assets/Brand/FlickLogo.png` is the exact PNG supplied by the
user as the corrected logo basis on 2026-10-30. Its sunset-to-purple/cyan circle,
letterform, glow, and black canvas are supplied artwork, not a generated redraw.
The source is 1254 by 1254 pixels, 8-bit RGBA, 1,941,835 bytes, with SHA-256:

```text
a3c1ec0879f93f94acb90c16e18eb2509289a94cfaaee4152e1ea81971fd6692
```

The original upload is also retained unchanged in the local evidence folder
`artifacts/ux-20261030/logo-supplied/FlickLogo-approved-20261030.png`. Both copies
match the source hash. Every PNG chunk CRC validates. The former 128-pixel PNG
had an invalid IDAT CRC; it has been replaced by this explicitly approved image
rather than repaired or reinterpreted. Its historical SHA-256 was
`86d0c488d02e88d8a8258e116251deef8c0084b1a88b887b079258f0288f38a3`.

The shared brand Image maps the complete source canvas to `Rect="0,0,38,38"`
within the existing centered 38-DIP circle. The old `Rect="1,1,38,38"` correction
belongs to the superseded bitmap and is removed. The backing canvas is now black
(`#000000`), matching the new image. No crop of the source rectangle, new glow,
recoloring, shader, aspect distortion, or extra translation is applied. The
existing outer circular viewport gives all app surfaces the same framing.

`scripts/build_flick_icon.ps1` uses that same zero-offset, full-source mapping for
16, 24, 32, 48, 64, 128, and 256-pixel ICO entries. It keeps the existing circular
outer frame and high-quality deterministic Windows resampling. The native tray
conversion must use the same black backing and zero-offset full-source mapping;
it must not retain the former `size/38` translation. The ICO is generated during
the Windows build and remains an ignored build output; it supplies the packaged
executable, title bar, taskbar, shortcuts, and installer. Windows icon-cache
behavior still needs checking on an actual upgraded installation.

The source image and code mapping were inspected locally. Updated Windows WPF
raster, native HICON conversion, packaged ICO entries, and installer checks are
pending the integrated Windows build for this source; the earlier renders below
do not qualify the newly supplied artwork.

## Renderer scope

`tools/Flick.UiRender` renders actual compiled WPF controls in an isolated Windows
GitHub Actions job. It never shows the main Window, so its Loaded handler does
not start capture discovery. It creates only synthetic clips, thumbnail graphics,
and a sample monitor in its temporary fixture profile. The renderer now seeds
the real library storage and view-model target bindings so counts and selection
match those fixtures. It paints the actual Window background behind client
content, which avoids transparent gaps in content-only screenshots.

The output includes the workspace, Trim, Audio, Share, Recording settings,
General settings, the General page scrolled to Software Updates, and the real
notification control with its default "Clip Saved" / "30 sec" text. No updater
network request is made. Brand samples use the real shared style rasterized by
WPF at 96, 120, 144, and 192 DPI (100, 125, 150, and 200 percent). These raster
samples are not a physical per-monitor DPI session. Each artifact records the
application informational version and exact GitHub commit in `PROVENANCE.txt`.
The native tray image invokes the actual `CreateTrayIcon` conversion and renders
its HICON to PNG without creating a tray entry. It verifies the runtime conversion
path, not Windows' final selection/scaling of a tray or packaged executable icon.

The images do not include gameplay, an active media decoder, the native Windows
title bar, or a running recording host. They show client-area layout from the
code; hardware, keyboard, native DPI transitions, and gameplay acceptance remain
separate checks.
