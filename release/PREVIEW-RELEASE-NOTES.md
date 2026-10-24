# Flick 1.0.6 Preview 1 — Replay Hotkey & Library Safety Fixes (UNSIGNED)

This Windows 11 x64 preview improves the reliability and clarity of replay controls. **Recording remains OFF on every launch**, including automatic Windows startup. Choose a source, press **Start Replay** (default `Ctrl+Shift+F9`), wait for **Replay Ready**, then press **Save Clip** (default `Ctrl+Shift+F8`) to save the preceding replay. A save hotkey pressed while replay is off or starting now reports an actionable failure rather than silently updating a footer.

## Changes from 1.0.5

- Save Clip stays disabled in the capture bar and tray until the native replay state is **Armed / Replay Ready**, not merely Buffering.
- The save hotkey displays a failed-save notification when replay is off, starting, or faulted; it never fabricates a successful clip.
- Windows shortcut-registration failures retain the actual Win32 error and conflicting chord information.
- A clip rename metadata-write error now reports an in-app failure instead of propagating through the WPF click handler.
- The delete-original confirmation now explicitly warns that its associated exported MP4 may also be permanently removed. Existing deletion semantics are unchanged.
- Windows WPF regression coverage includes Save button readiness and replay-off feedback.

The 1.0.5 recording improvements remain, including NVIDIA NVENC / AMD AMF / Intel Quick Sync auto selection and an optional bounded x264 CPU fallback, replay packet readiness, media verification, and safer index handling. No native recording algorithm or media codec is changed in this patch release.

## Installation and updates

Use **Flick-Setup.exe** for normal per-user installation over any existing installed Flick preview without uninstalling first. Preserve your recordings, preferences and library. Installed 1.0.4/1.0.5 users may check **Settings → General → Software Updates** with preview releases enabled; older builds may require a one-time manual installer upgrade. Portable users should quit Flick and replace the extracted portable folder. Keep the packaged OBS, FFmpeg, and .NET files together.

Requires Windows 11 x64, Direct3D 11-compatible graphics drivers, and the Microsoft Visual C++ v14 x64 Redistributable. Check the downloaded installer against the release's **SHA256SUMS.txt** before running.

## Important preview limitations

**UNSIGNED:** This developer preview has no authenticated Windows publisher signature. Windows Smart App Control may block installation or execution, and SHA-256 checksums do not give publisher trust. Do not disable Windows security protections to install Flick; a Store-signed MSIX or trusted signed GitHub installer will require separate validation.

**HARDWARE ACCEPTANCE PENDING:** Passing GitHub-hosted Windows CI, synthetic FFmpeg tests, UI rendering and installer lifecycle checks does not prove hotkeys, NVENC/AMD/Intel recording, in-game overlays, or GPU/FPS behavior on a physical Windows PC. Owner-hardware acceptance and endurance tests remain outstanding. Do not describe this version as stable or fully compatible. See [GPU compatibility testing](../docs/WINDOWS-GPU-COMPATIBILITY.md) and [release readiness](../docs/RELEASE-READINESS-1.0.6.md).

This preview remains GPL-2.0-or-later for original Flick code, with separate upstream third-party licenses and notices. No telemetry, required account, automatic recording at launch, or silent uploads are introduced.

The publication workflow must pin the exact source commit/tree, successful Windows CI/render/package runs, artifact archive digest, Setup SHA-256 and validated public downloads. These draft notes alone do not authorize publication.
