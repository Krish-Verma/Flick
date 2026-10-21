# Flick — Memory and Preview Optimization (2026-10-21)

**Implementation branch:** feat/flick-installer-open-source-performance-20261021, draft PR #8 stacked on GUI Slice 6 PR #7. The source changes are implemented. **No memory savings are claimed without Windows A/B traces.**

## Existing measured baseline (AntiGravity pre-optimization report)

- Idle app working set: **163.1 MiB**
- Idle app private bytes: **101.3 MiB**
- Idle app CPU: **0.00%** over a bounded sample
- Reported idle app handle count: **907**, thread count **28**
- Original GUI engineering target: **120 MiB working set**, which is not currently met.

Working set, private bytes, GPU VRAM and native capture-host bytes are different metrics. Do not add them or call the 163.1 MiB app working set a capture-buffer allocation. Baseline sample used existing application state and specific PC hardware, not a universal prediction.

## Source changes

### 1. Small, bounded thumbnails

**src/LocalClip.Core/Media/MediaProber.cs** now asks FFmpeg for a single JPEG scaled within a **320x180** frame while preserving source aspect ratio. Import/clip generation previously produced JPEGs at source video resolution. New thumbnails use limited storage and can be decoded quickly.

**src/LocalClip.App/Services/ThumbnailImageConverter.cs** decodes the image at a capped **256-pixel width** using BitmapImage.DecodePixelWidth, OnLoad and Freeze. The converter handles old high-resolution thumbnails too, without deleting or rewriting originals or user caches. Each realized clip-row image and selected-clip poster uses it; the list already virtualizes/recycles rows.

**Expected user effect:** thumbnail appearance remains visually comparable at 74x48 DIP rows, with lower peak decoded-image memory; cost is minor thumbnail resizing on initial generation. No claim that old on-disk thumbnails immediately shrink or that RAM decreases by a specific number.

### 2. Lazy media decoding while browsing

**src/LocalClip.App/MainWindow.xaml.cs** no longer sets the native MediaElement.Source every time the user selects a clip. Instead, the editor displays a low-memory poster until Play, Original Preview or Export Preview is requested. It unloads the decoder on clip changes and when the window is hidden/minimized.

**Expected user effect:** fewer idle decoder resources and less incidental media I/O; first playback may initialize a little slower. Scrubbing, compatibility proxy generation, export and media error/fallback handling must be verified on Windows. The original video is immutable.

### 3. Debounced library search

The SearchQuery WPF binding now delays text commits by **180 ms** rather than rebuilding the visible list for each rapid keypress. The existing virtualized list and in-place import ownership safeguards are preserved.

**Expected user effect:** smoother typing in large libraries, no visual reductions; results update after a short delay.

### 4. Correctly dispose transient process samples

**src/LocalClip.Core/Diagnostics/ResourceMonitor.cs** now disposes the Process object acquired for a resource sample. A resource handle should not remain owned after a sample; the capture worker, timestamps and hard safety cap remain unchanged.

**Expected user effect:** healthier handle lifetime during prolonged capture. Little reason to expect large idle-RAM savings because sampling mainly occurs while capture runs.

## Deliberately unchanged

The native OBS/NVENC replay-buffer configuration, FFmpeg export validation, virtualized clip list, source privacy, custom UI colors, logo, ~2-second overlay/chime, and original recording semantics remain unchanged. No constant on-screen overlay, blur shader or additional always-running process was introduced. Closing the UI hides it to tray rather than quitting; measured tray memory may still include WPF visual objects until process exit.

## Reproducible measurement protocol for AntiGravity

Compare **pre-change PR #7 fd937f196b3f0ece75b7c77c639e2bca39c15d05** to the **exact checked-out PR #8 head**, with identical Windows account, hardware, drivers, clips, display scale, app/window states and 3+ repeated runs per case.

1. Record an **empty-library, no clip selected, replay-off** five-minute baseline in both builds.
2. Record **50 / 1000 / 5000 indexed clips** with matching thumbnails (and explicitly distinguish fresh bounded thumbnails from old legacy JPEGs). Observe working set/private bytes/handles/CPU, search responsiveness, scroll and thumbnails.
3. Record **selected but never played**, after 30 seconds, then playing/scrubbing, then stopped, minimized/hidden-to-tray, restored and finally fully quit. Verify no decoder/FFmpeg worker while idle.
4. With capture enabled, compare app/host working set, private bytes, handles, CPU, GPU encoder and VRAM. Do not increase replay buffer, background decoding or dropped frames.
5. Record real game FPS, 1% lows and p95/p99 frame times with the same scripted game scene and settings (Capture Off / On, Overlay Off / On). The app sampler does not measure FPS.
6. Use scripts/sample_flick_performance.ps1 for per-process CSV; annotate exact PID, phase, source SHA, elapsed sample timestamps, sample interval, Windows DPI/scale, fixture and cache state.

**Pass conditions:** actual app RAM is meaningfully improved or a grounded blocker is recorded; ideally return to the original <=120 MiB idle working-set target, but do not redefine a 163 MiB measurement as <=120 MiB. Preserve functional behavior and protect clip sources. The <=120 MiB target is a goal, not an automatic unqualified release gate for all libraries.

If the source optimizations cause a regression, AntiGravity must capture reproduction, logs and counterexamples and report them. **Only the development owner edits the source.**

See the complete [Antigravity QA Handoff](FLICK-PREVIEW-ANTIGRAVITY-TEST-HANDOFF.md).
