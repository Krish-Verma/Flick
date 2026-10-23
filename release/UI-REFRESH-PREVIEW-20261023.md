# Flick UI Refresh — October 23 Preview (UNSIGNED)

**Latest redesigned preview, for authorized testers. This installer is UNSIGNED and Windows Smart App Control may block it. Do not disable Smart App Control or Defender to install it.**

Download **Flick-Setup.exe** below for normal installation, or **Flick-v1.0.2-win-x64.zip** for portable use. Both contain the complete R1–R8 UI refresh. The filename and embedded application version remain 1.0.2-preview.2; this distinct dated preview tag identifies the new source candidate and does not overwrite the earlier Preview 2 release.

## What's new

- Embedded Settings and Connected Folders in one main window.
- Native dark title bar, circular original logo, themed scrollbars and menus.
- Video-first workspace with evenly spaced Trim, Audio, Original and Share tools.
- Contextual editing and file-first MP4 Share, with optional explicit Discord posting.
- Deliberate shortcut recording with Save/Cancel and reserved/duplicate validation.
- Themed in-window confirmations and rename.
- Save banner containing only **Clip Saved** and actual rounded duration; independent sound settings, built-in quiet tones and bounded local PCM WAV selection.

## Verification and limits

Source: `79204f67ca9318526873730946d82556eee9ce9c`.
Windows CI: https://github.com/Krish-Verma/Flick/actions/runs/39977197033 — **141 passed, 0 failed, 7 hardware tests skipped**; WPF release build, native compile and publish smoke passed.
Private packaging: https://github.com/Krish-Verma/Flick/actions/runs/39977197114 — Setup, portable ZIP and checksums passed. GitHub packaged synthetic PR merge `7d6c83f479857bc192666610aaf4fbb2732b07ec`; its source tree is identical to the head above.

Setup SHA-256: `E09BEC570628D664AEC3FBDD5A53B2CF363D8C35A28B1E1205573C46CB1EF411`.
Check both downloads against **SHA256SUMS.txt**.

Actual Windows DPI/screenshots, keyboard, capture, gameplay performance and endurance acceptance remain pending. This is **not a signed, hardware-qualified or stable release**. Recording support remains Windows 11 x64 with compatible NVIDIA NVENC hardware. No AMD/Intel-only or ARM recording support is claimed.

Installation keeps application files separate from settings, library metadata and recordings. Quit Flick before installing over the prior version. Existing upgrade/uninstall protections still require local acceptance on this refreshed candidate. Automatic in-app updates are not implemented yet.

Full test-only acceptance handoff:
https://github.com/Krish-Verma/Flick/blob/79204f67ca9318526873730946d82556eee9ce9c/docs/GUI-REFRESH-R3-R8-ANTIGRAVITY-HANDOFF.md

The repository is currently private. This prerelease is available to repository members; making the repository public is a separate owner action. No personal recordings or credentials are included in the assets.
