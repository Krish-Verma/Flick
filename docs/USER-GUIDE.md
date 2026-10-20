# Flick — User Guide (Development Build)

> Flick is currently in development. This guide describes the source on the draft stabilization PR, **not** the published v1.0.0 release. Real Windows/GPU and Discord acceptance are still required before general distribution.

Flick is a small, free, offline-first **Windows 11 x64** gameplay clipper. It manually captures recent action, lets you review and trim the original, adjust audio for the share copy, and prepare a Discord-sized MP4 without a watermark. Initial hardware support targets **NVIDIA NVENC**; other GPUs are not qualified.

## 1. Install or run

After the source and native OBS dependencies are rebuilt and validated on Windows, launch `LocalClip.App.exe` from the self-contained portable package or install it per-user using `install.ps1`. The current ZIP is still a **development artifact**, not a fully accepted end-user installer. Uninstalling Flick must not delete your recordings.

Flick stores settings and metadata under `%LOCALAPPDATA%\LocalClip`; originals are saved by default to `%USERPROFILE%\Videos\LocalClip\Originals`. You may change the originals folder in Settings.

## 2. The everyday clipping workflow

1. Open Flick or restore it from its tray icon. **Capture is OFF on every launch**, including when Windows starts Flick. Selecting a target or minimizing the window never starts recording.
2. Choose a specific monitor or game window in the target dropdown. Click **Arm Replay**, or press **Ctrl+Shift+F9** (configurable).
3. Play normally while Flick fills a bounded replay buffer. The UI displays an **estimated** buffered duration; a 256 MiB compressed packet limit can shorten a high-bitrate, long clip.
4. When you want to capture a moment, press **Ctrl+Shift+F8**, or click **Save Clip**. Flick saves the recent replay to an MKV original with a unique filename; subsequent saves do not intentionally replace old originals. Saving does **not** stop ongoing replay capture.
5. Select your saved recording in the gallery. Play/seek, rename, search, open its folder or explicitly delete it. Choose **Open Player** if Windows cannot preview MKV in its built-in MediaElement.
6. Adjust **Trim Start / End** and the recorded audio tracks' **Mute / Gain** values. These are saved edit decisions; the MKV original is unchanged. The editor's original playback may not reflect the export mix until you export.
7. Pick **20 MB**, **50 MB**, **100 MB**, or an editable **Custom 1–250 MB** target. Click **Export for Discord**. Flick makes a new H.264/stereo AAC MP4, verifies its duration, size cap and decode, and keeps the MKV original intact.
8. Click **Export MP4** in the player to preview the share copy and its audio mix. Then drag the ready file into the Discord desktop composer or use **Copy File** and paste (where supported). **Flick does not automatically send a message.**
9. Click **Disarm Replay** to release the encoder, or quit Flick through the tray menu to stop all owned helpers. Closing the window hides it to the tray; minimizing is not the same as stopping capture.

## 3. Small, useful settings

Open **Settings** in the main window. Changing recording settings stops the current buffer after confirmation; you must manually arm again.

| Setting | Options and behavior |
|---|---|
| Replay duration | 15–120 seconds; default **30s** |
| Recording quality | **Light** (720p/30 FPS, 6 Mbps), **Balanced** (1080p/30 FPS, 15 Mbps), **Smooth** (1080p/60 FPS, 20 Mbps) |
| Memory budget | Compressed replay packet budget 256 MiB; settings warn if a profile is likely to shorten the requested replay |
| Microphone | Off by default; opt in to record it |
| Sound | System audio loopback plus optional mic; this is **not** independently isolated game/Discord audio |
| Hotkeys | Separate customizable Capture Toggle and Save Clip keys; incompatible or conflicting registrations show errors |
| Saved clips | Select a full path for the originals folder; completed MKVs are not deleted when settings reset |
| Windows startup | Optional per-user sign-in registration; always starts **disarmed** |
| Open in tray | Optional hidden launch with tray controls, separate from capture status |
| Discord sharing | 20/50/100/custom MB file cap presets; optional encrypted webhook requires explicit confirmation for posting |

Long/high-quality replays may exceed the compressed packet budget and retain less history. A video’s settings describe the requested profile, not a guarantee of actual hardware FPS or image quality. Flick does not silently switch to CPU-heavy x264 capture when NVENC cannot start.

**Restore Defaults** resets the fields in the settings window; press **Save Settings** to apply them. **Cancel** leaves settings unchanged. Existing originals are never deleted by either action.

## 4. Limitations and troubleshooting

- **No video captured:** Confirm replay is manually armed, the correct target is selected, and the NVIDIA hardware encoder is available. Closing the selected window must not silently fall back to a monitor.
- **Clip is shorter than selected duration:** Replay duration is bounded by capture startup time, keyframe boundaries and the 256 MiB compressed buffer. Try Balanced or Light, or shorten the requested replay.
- **Audio sounds doubled:** Use the dedicated system/mic tracks for export; don't mix the combined preview track with its source tracks. Check headset monitoring if mic sound is already in system audio.
- **MKV playback fails:** Use the **Open Player** fallback, then preview the exported H.264/AAC MP4.
- **Hotkey unavailable:** Choose a different combination in Settings; Windows may reserve shortcuts used by another program.
- **Export unavailable:** Review the file cap, selected trim, storage space and encoder. Invalid exports must not be marked Ready.
- **Discord upload fails:** Drag a fully validated MP4; use a smaller file-size cap if your Discord account or destination allows less.
- **Startup trouble:** Disable Windows startup in Settings. The registration is per-user; it never creates a scheduled recording session.
- **Performance:** Compare game FPS with replay Off and On on the same scene; record App/Host memory and GPU encoder use. Do not change unrelated PC services/drivers merely to improve numbers.

## 5. Verification for contributors

Read `ClipperBlueprint/WINDOWS-CLIPPER-BLUEPRINT.md` and `docs/ANTIGRAVITY-VALIDATION-HANDOFF.md` before modifying capture or publishing a release.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_tests.ps1
powershell -ExecutionPolicy Bypass -File scripts/run_tests.ps1 -Hardware
```

The second command requires the owner's explicitly qualified Windows/NVIDIA setup; missing hardware must **fail**, not silently pass. `scripts/check_motion.ps1` samples video frames for motion but does not replace watching real gameplay, listening to each track and testing the full installed app and Discord drag/paste.

Flick is intended to become freely available and open-source under GPL-2.0-or-later after owner-approved release qualification. See `LICENSE` and `THIRD-PARTY-NOTICES.md`.
