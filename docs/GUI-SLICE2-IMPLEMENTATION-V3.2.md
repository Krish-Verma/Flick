# Flick GUI — Slice 2 Implementation (v3.2)

**Review branch:** `feat/gui-slice2-shell-notifications-20261020`  
**Draft PR:** https://github.com/Krish-Verma/Flick/pull/3  
**Base:** `feat/gui-slice1-design-foundation-20261020` ([Slice 1 PR #2](https://github.com/Krish-Verma/Flick/pull/2))  
**Backend ancestor:** [PR #1](https://github.com/Krish-Verma/Flick/pull/1)  
**Approval boundary:** Only Slice 2 was authorized. Do not implement Slice 3 importing or redesign the editor/settings architecture yet.

## Product intent

The existing WPF app remains a lightweight manual-first Windows gameplay clipper. Keep working global hotkeys, source selection, start/stop, saved clips, trim/audio controls, original/export previews and drag/copy-to-Discord. **Replay remains disarmed on every app launch**, including Windows startup; this GUI slice does not change native libobs or FFmpeg recording/export code.

### Shell

- Add fixed 60-DIP navigation rail with the full original Flick squircle at the top, **Your Clips** and **Settings** buttons. Your Clips focuses library search; Settings opens existing preferences until the dedicated Settings layout is implemented in Slice 5.
- Add compact 64-DIP shared-token frosted capture bar: source dropdown/refresh, one true status pill, estimated buffer text, Start/Stop Replay and Save Last N Seconds. Save becomes primary only while buffering/armed. Save is disabled when replay is off.
- Main library panel is 286 DIP preferred, adjustable via existing splitter; keep old editor functionality intact for later dedicated visual redesign.
- Use the Slice 1 Sunset/Coral/Magenta/Violet/Indigo logo identity in low-saturation graphite surroundings. Avoid neon backgrounds and heavy blur.
- Present performance diagnostics only in the existing bottom status line. Title Case controls; no extra Capture or Export page.
- Current source change while capturing now asks permission before resetting unsaved replay; declines preserve the existing source.

### Notification and sound

- MainViewModel's `ClipSavedConfirmed` fires only after the native save callback file exists, media probe/thumbnail registration succeeds, and the library is updated. A native save request acknowledgement alone never triggers a success toast/sound.
- `ToastOverlayWindow` hosts the reusable `FlickClipToast`, using the entire user-supplied squircle logo (no inner circular badge or gradient divider). It is a **temporary**, non-activating, click-through, topmost layered WPF window approximately 294×80 DIPs including insets; toast chrome is roughly 278×64.
- `FlickNotificationService` creates this window only upon confirmed success or explicit preview, displays it for about two seconds with a short fade, closes it to release resources and coalesces repeated notifications (no stacking). No game injection, background screen capture, always-running renderer or additional process.
- Audio is a short (~180ms), quiet, lazily generated in-process PCM two-note WAV played with `SoundPlayer`. No streaming/recording service or additional bundled media library.
- AppSettings stores `ShowClipSavedOverlay` and `PlayClipSavedSound` separately, **both true by default**. Each preference is independently respected; changing them does not activate recording. Old JSON files lacking the keys receive true defaults. A manual Preview Notification button is intentionally available without committing unsaved preferences.
- Save failures surface as **Couldn't Save Clip** with error styling and **no success sound** when overlay feedback is enabled. Core capture fault handling remains separate; failed save does not falsely appear as success.
- No duplicate Windows tray success balloon is shown before registration. If the custom notification cannot be displayed, the app may show a basic Windows notification after confirmation.

### Tray and settings

- Brand the existing tray icon using the original embedded logo, with safe system icon fallback. Tray items include Open Flick, Start/Stop Replay, Save Last N Seconds, Settings and Quit Flick. Enabled states follow actual replay status.
- Closing the main window hides it to the tray (one short explanatory hint per session); quitting releases the app's notification/owned media objects.
- Opening Settings does **not** disarm a running capture. Profile changes apply at the next manual replay start. Explicitly disabling a live microphone requires confirmation that current replay buffer will be stopped/discarded, and then releases it.
- The original gallery/editor sharing UX is deliberately preserved. In this slice, Settings still appears as a modal dialog; full in-window Settings navigation is a future slice.

### Known unverified constraints

- Windows Mica backdrop acceptance is attempted on the main window, but visible frosted material depends on OS/WPF/window backgrounds; no GPU shader is added.
- Exclusive-fullscreen games may hide a normal desktop overlay; do **not** add injection or anti-cheat-sensitive hooks to force it visible. Borderless/desktop modes require real user testing.
- Transparent WPF window positioning with multiple monitors and mixed DPI, game focus and overlay capture visibility require Windows acceptance.
- Confirmation chime can enter system-loopback capture while replay is armed. Verify audibly and honor the sound-off setting.
- Windows CI checks compile/static code; does NOT establish overlay FPS impact or real hardware visibility.

## Performance gates (measure against a stable baseline)

The prior owner-supplied benchmark reported 48.2 MiB idle app working set and 142.5 MiB combined buffering working set. This is **historical baseline evidence, not a Slice 2 measurement**.

Target additional idle RAM with the overlay enabled but hidden ≤2 MiB, temporary peak overlay RAM ≤15 MiB, <1% average game FPS impact, no persistent CPU/GPU rendering when overlay hidden, no noticeable frame-time spikes and no extra persistent process. These are targets, not guaranteed results. Compare capturing game with toggles On versus Off and report identical-scene FPS, 1% lows and p95/p99 frametimes, private bytes/working set/VRAM separately.

## CI and approval

- CI: `dotnet build LocalClip.slnx -c Release --warnaserror`; `scripts/run_tests.ps1`, `scripts/check_gui_design.ps1` and `scripts/check_gui_slice2.ps1`, WPF portable publish and native MinGW compilation.
- Hardware: see `docs/GUI-SLICE2-ANTIGRAVITY-HANDOFF.md`, including exact screenshots and non-activating overlay tests.
- Keep PR #3 draft, not merged or released, until owner reviews screenshots and AntiGravity evidence. Slices 3–6 stay unstarted until separately authorized.
