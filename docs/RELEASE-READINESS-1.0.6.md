# Flick 1.0.6-preview.1 release readiness

## Scope

This candidate carries focused replay hotkey and library-action fixes from PR #33 on top of the published 1.0.5 recording engine. It does **not** change native OBS encoder selection, replay packet retention, audio tracks, default disarmed-on-launch behavior, file format, folder migration or existing user-clip deletion semantics.

- Save is only enabled in the UI/tray when the native helper reports Armed/Replay Ready.
- Pressing a save hotkey while replay is Off, Starting or Faulted reports an actionable failed-save notice through the existing event; detailed fault status is preserved.
- The save hotkey's Win32 registration error is no longer hidden by a generic message.
- The clip-rename UI catches an index-write failure and reports it safely.
- The clip-delete confirmation discloses permanent deletion of any associated exported MP4.
- Windows STA WPF regression coverage checks replay save readiness and off-state feedback.

## Required release source and artifact evidence

Before publication, record the exact final source SHA/tree and successful **same-source** Windows build/tests, WPF rendering and installer/package lifecycle workflows. Confirm warnings-as-errors build, test failures = 0, all expected hardware-independent tests, exact product/version metadata for Flick.exe/Flick.dll/LocalClip.Core.dll/native CaptureHost, complete OBS/FFmpeg payload, package manifests/checksums, and safe upgrade/uninstall data retention. Pin the final artifact ID and archive digest and reviewed Setup SHA-256; release publishing must reject any mismatches, existing candidate tag or unexpected release catalogue.

The new release is a separate `v1.0.6-preview.1` tag. Never replace the immutable 1.0.5 release's executable bytes or reuse the previous version's package filename. Make only the new verified preview the GitHub Latest; retain the prior 1.0.5 tagged release and assets as an authenticated draft for rollback/CI rather than deleting source history or original installers.

## Physical Windows acceptance not provided by hosted CI

- Test with a visible selected game/monitor, keyboard in genuine gaming focus and actual Ctrl+Shift+F9 then Ctrl+Shift+F8 hotkeys. Verify Off => failed-save notice, Buffering => disabled UI/tray Save, Armed => original MKV created, indexed, decoded and uniquely named.
- Check hotkey conflict, compact keyboard Fn layer, borderless/exclusive fullscreen, tray-hidden operation, repeated saves, launch/reset and target loss.
- Capture/encode/audio verification with target Windows GPU/driver combinations (NVIDIA, AMD, Intel, hybrid and bounded CPU fallback). Measure in-game FPS/1% lows, encoder load and 2h/8h endurance as separately authorized.
- Verify microphone opt-in and system-audio tracks, full MP4 H.264/AAC exports, Discord drag/paste, installer upgrade, data-preserving uninstall and manual update flow.
- Confirm errors and clip notification placement in high-DPI/fullscreen and accessibility modes.

This remains an **unsigned developer preview** until trusted signing is available and separately verified. GitHub Latest means newest available preview, not Microsoft publisher trust or stable hardware certification. Smart App Control may reject it; do not present checksums as a trust substitute or ask users to lower security protections.

## Future distribution

The Microsoft Store MSIX option is tracked independently as issue #34; packaging, app identity, helper execution, Store-managed update/uninstall, profile migration and actual Store certification are not included in 1.0.6. The conventional GitHub Inno Setup file will not become Microsoft-signed merely because a separate MSIX package is later Store-signed.

Original Flick source remains GPL-2.0-or-later. OBS/libobs, FFmpeg and bundled binaries retain their respective notices, copyright and corresponding-source obligations. User data and permission policy must not change during a version-only release.
