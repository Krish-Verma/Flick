# Flick 1.0.8 Preview 1 — Settings, Replay & UI Reliability (UNSIGNED)

Flick 1.0.8 is a Windows 11 x64 development preview combining fixes to everyday clip saving, explicit audio-device selection, and settings persistence with subtle interaction polish.

## What's new

- **Settings Save is now verifiable.** All seven settings categories stage their values, write a complete reviewed JSON snapshot, read it back, and report failures instead of silently appearing to save. Windows startup registration is attempted only when the relevant preference changes. If it fails, Flick warns you but can still save independent choices such as **Show RAM and CPU**, **Minimize to Tray**, replay quality/duration, sharing, and notification options.
- **Tray/minimize controls now work as described.** **Minimize to Tray (also start hidden)** hides Flick on a real minimize action as well as at next launch. Manual recording stays off unless started intentionally; approved-game auto-recording remains separately opt-in. Closing with X still hides in the tray, and Quit is explicit.
- **Preview-update choice is retained.** Include preview releases now persists across Settings reopens, using the same Save/Cancel semantics as other preferences.
- **Clearer interactive interface.** Small purple hover/focus cues on buttons, Settings navigation, menus, checkboxes, selectors, sliders, library/folder rows, and the ready-to-drag Share tile help identify clickable areas. Animations are event-driven, short, and respect Windows reduced-motion/High Contrast settings.
- **Clip save feedback is more truthful.** A global Replay Toggle command cannot incidentally stop an in-flight save; a repeated save while native save is still outstanding is reported as pending, not as a false second failure. A missing IPC acknowledgment is not automatically a definitive save failure while the native helper still tracks it. Verified, durably indexed MKV originals now produce the success banner without first waiting for optional thumbnail generation. Private enum-only diagnostics record whether a replay command came from a global shortcut, toolbar, tray, settings, or quit, without logging raw keys.
- **Actual audio device choices.** Settings → Audio lists active Windows speakers/headsets and microphone inputs, with Windows-default and explicit endpoints. Missing devices are shown as unavailable rather than silently redirected. Selecting a microphone doesn't turn it on: recording remains opt-in, and separate per-app Game/Voice audio choices are preserved.

The previously introduced v1.0.7 local-first library, game profiles, nondestructive editor, audio mixing, size-capped MP4 export and Discord file drag/copy features remain available.

## Upgrade

Install **Flick-Setup.exe** over the previous per-user installation. Do **not** uninstall your prior copy first. The installer preserves the existing settings, recording library, originals, exports and imported media. For portable use, quit Flick before replacing the extracted package with **Flick-v1.0.8-win-x64.zip**; retain the complete OBS, FFmpeg and .NET runtime files. Verify downloaded bytes against **SHA256SUMS.txt**.

The default original recording folder remains under Videos/LocalClip/Originals; profile settings stay under %LOCALAPPDATA%/LocalClip. No account, telemetry or automatic uploads are introduced.

## Limitations — please read

**UNSIGNED DEVELOPER PREVIEW.** Neither the installer nor app has a trusted publisher signature. Windows Smart App Control may block it. SHA-256 verification proves file integrity, not publisher trust; **do not disable Windows protections** to install this preview. Code signing and Microsoft Store packaging are separate projects.

**REAL-HARDWARE ACCEPTANCE PENDING.** Windows CI validates the application build, unit/WPF tests, synthetic media, native compilation, UI rendering and installer upgrade. It does **not** establish success of one-press saves during every game, real NVIDIA/AMD/Intel GPU capture, fullscreen notification, headset/microphone device behavior, Discord desktop drag/paste, or game FPS/endurance. The owner is performing those physical gaming-PC tests after this preview. The previous single-press failure's exact root cause was not proven; the changes address verified timing/feedback vulnerabilities and improve future diagnosis.

Target: Windows 11 x64, suitable Direct3D11 graphics drivers and the Microsoft Visual C++ v14 x64 runtime. Source license GPL-2.0-or-later with independent third-party OBS/FFmpeg notices and obligations.

**This is the newest *preview*, not a stable, signed or hardware-certified release.**