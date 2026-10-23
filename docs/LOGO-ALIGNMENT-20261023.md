# Shared logo alignment and actual WPF renders

The original `Assets/Brand/FlickLogo.png` remains unchanged. The shared brand
Image style now uses a fixed 38-DIP DrawingImage viewport, moving the original
bitmap one DIP right and one DIP down within its centered circular clip. This
compensates for the visible artwork's optical mass being about three source
pixels above/left of the 128-pixel source canvas center. The Image itself and its
circle remain centered in navigation, Settings, and clip notifications. The
existing sunset artwork and restrained graphite/lavender theme are preserved.

The canvas color under the shifted image is the original bitmap's corner color,
`#06030D`. A fixed drawing rectangle and circle keep the resulting image exactly
38 by 38 DIPs; no extra badge, recoloring, stretching, shader, or blur is added.

The native tray icon and every packaged ICO size (16, 24, 32, 48, 64, 128, and
256 pixels) apply the same normalized correction: the image moves by `size/38`
pixels inside a fixed dark circular viewport. The tray path draws from the
original embedded PNG; `scripts/build_flick_icon.ps1` generates the native ICO
used by the executable, title bar, taskbar, shortcuts, and installer. Transparent
pixels remain outside the circle. These generated icons do not replace or modify
the original PNG. Native Windows icon-cache behavior still requires checking on
a real upgraded installation.

The canonical original PNG has an invalid IDAT CRC and fails strict Pillow
decoding. Windows WPF decoded it in the previous renderer run. The source is
preserved rather than silently replacing or repairing the artwork. This finding
should be considered if a future icon-export pipeline uses a stricter decoder.

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
