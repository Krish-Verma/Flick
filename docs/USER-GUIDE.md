# Flick — User Guide (Development Build)

> Flick is currently a private developer preview. This guide covers the stacked GUI Slice 6 and PR #8 open-source preparation. Source CI is not equivalent to a fully qualified public Windows installer. Owner release approval is still required.

Flick is a small, free, offline-first **Windows 11 x64** gameplay clipper. It manually captures recent action, lets you review and trim the original, adjust audio for the share copy, and prepare a Discord-sized MP4 without a watermark. Initial hardware support targets **NVIDIA NVENC**; other GPUs are not qualified.

## 1. Install or run

Two preview formats are available after a verified Windows build: extract `Flick-v1.0.1-win-x64.zip` and launch **Flick.exe**, or run the unsigned `Flick-Setup.exe` wizard for a normal **Start Menu** entry, optional desktop shortcut and Windows **Installed Apps** uninstall registration. The Inno Setup build is not released publicly until AntiGravity tests it. **Do not run the previous `LocalClip` `uninstall.ps1`**, which could erase the directory containing user metadata. New program files install in `%LOCALAPPDATA%\Programs\Flick`, separately from settings and clips.

Flick stores settings and metadata under `%LOCALAPPDATA%\LocalClip`; originals are saved by default to `%USERPROFILE%\Videos\LocalClip\Originals`. You may change the originals folder in Settings.

## 2. The everyday clipping workflow

1. Open Flick or restore it from its tray icon. **Capture is OFF on every launch**, including when Windows starts Flick. Selecting a target or minimizing the window never starts recording.
2. Choose a specific monitor or game window in the target dropdown. Click **Start Replay**, or press **Ctrl+Shift+F9** (configurable).
3. Play normally while Flick fills a bounded replay buffer. The UI displays an **estimated** buffered duration; a 256 MiB compressed packet limit can shorten a high-bitrate, long clip.
4. When you want to capture a moment, press **Ctrl+Shift+F8**, or click **Save Last 30 Seconds** (or your configured duration). Flick saves the recent replay to an MKV original with a unique filename; subsequent saves do not intentionally replace old originals. Saving does **not** stop ongoing replay capture.
5. Select your saved recording in **Your Clips**. The player first displays a lightweight thumbnail **poster**, without loading its video decoder. Press **Play** to open the Original. Use **Play/Pause**, the seek slider or its arrow keys, and the small **Original / Export Preview** dropdown. If Windows cannot decode the MKV, click **Create Local Preview** for an explicitly requested, bounded H.264 MP4, or **Open in External Player**. Originals are never changed by preview generation.
6. Use the **single Trim Clip timeline** to adjust both endpoints with drag handles, Tab/arrow keys (0.1s), Ctrl+arrow (1s), or precise Start/End fields in seconds or M:SS.ff. Click **Reset** to select the full clip; the minimum segment is 0.25s. Expand **Export Audio** to mute, adjust gain (0–200%) or Reset Mix. Trims persist after handle release, and audio changes save after a short pause. The original recording stays unchanged.
7. In the compact **Export for Discord** dock, choose the **20 MB default**, **10/50/100 MB** or **Custom 1–250 MB** decimal size limit, then click **Export Clip**. The honest progress stage appears while FFmpeg runs; **Cancel Export** stops owned encoding without altering the original. A successful export is verified as H.264/stereo AAC MP4 with correct duration, size cap and full decode.
8. Choose **Export Preview** in the player to review the final mix. Once a valid share file is Ready, **drag the MP4 file tile** directly into Discord, or choose **Copy File** to paste via Windows clipboard; **Open Folder** reveals the output directory. Dragging starts only after you move the mouse. Modifying trim, gain or file-size cap invalidates outdated Ready status. **Flick does not automatically send any message.**
9. Click **Stop Replay** to release the encoder, or quit Flick through the tray menu to stop all owned helpers. Closing the window hides it to the tray; minimizing is not the same as stopping capture.

## 3. Small, useful settings

Open **Settings** from the navigation rail or tray. It now has six compact categories: **Recording, Audio, Shortcuts, Storage, General, Sharing**. Only the selected category is visible. **Opening Settings does not stop recording.** Quality and duration changes apply the next time you manually start replay. Disabling an actively recording microphone requires explicit confirmation because it stops the current buffer to release the device.

| Setting | Options and behavior |
|---|---|
| Replay duration | 15–120 seconds; default **30s** |
| Recording quality | **Light** (720p/30 FPS, 6 Mbps), **Balanced** (1080p/30 FPS, 15 Mbps), **Smooth** (1080p/60 FPS, 20 Mbps) |
| Memory budget | Compressed replay packet budget 256 MiB; settings warn if a profile is likely to shorten the requested replay |
| Microphone | Off by default; opt in to record it |
| Sound | System audio loopback plus optional mic; this is **not** independently isolated game/Discord audio |
| Hotkeys | Separate customizable Capture Toggle and Save Clip keys; incompatible or conflicting registrations show errors |
| Saved Clips Folder | Choose where future replay MKVs are written; existing originals are not moved |
| Export Folder | Choose a separate location for future Discord-ready MP4s; existing exports are not moved |
| Connected Clip Folders | Link existing local MP4/MKV folders in place; nothing is uploaded or copied |
| Windows startup | Optional per-user sign-in registration; always starts **disarmed** |
| Open in tray | Optional hidden launch with tray controls, separate from capture status |
| Clip Saved Overlay | Enabled by default; shows the full Flick logo in a temporary (~2 second) click-through banner after a validated save |
| Confirmation Sound | Enabled by default, independently toggleable; quiet, short in-process chime (may be heard by system-loopback capture) |
| Preview Notification | A button to see/hear sample feedback without saving preferences or starting capture |
| Discord sharing | 10/20/50/100 MB and Custom cap; 20 MB default, optional encrypted direct webhook disabled by default and requiring explicit per-send confirmation |

Long/high-quality replays may exceed the compressed packet budget and retain less history. A video’s settings describe the requested profile, not a guarantee of actual hardware FPS or image quality. Flick does not silently switch to CPU-heavy x264 capture when NVENC cannot start.

**Restore Defaults** stages the factory preferences; press **Save Settings** to apply them. **Cancel** leaves saved preferences unchanged. Shortcut fields accept actual key chords, and each setting category uses Flick's shared typography and control sizes. Existing originals are never deleted by Restore or Cancel.

### Clip Saved Feedback

By default, successfully saved and indexed clips trigger **Clip Saved** in the upper-right corner of the active screen, using the full Flick squircle logo, the actual saved clip title/duration, and a short sound. The banner and sound are **independent** settings. Turning the banner off does not disable the sound; turning the sound off does not disable the banner. A failed save shows a different failure notification and never plays the success chime.

The notification is temporary and creates no permanent game overlay process. Native topmost WPF windows may not appear above every exclusive-fullscreen game; this behavior, mixed-DPI placement, and FPS impact still need validation on the owner's Windows PC. The banner should not interrupt gameplay focus.

### Window Layout, Scaling and Accessibility

At normal desktop widths (1120+ logical WPF pixels), Flick shows **Your Clips** on the left and the editor on the right. On narrower or highly scaled displays, Flick switches to a **compact one-pane layout**. Selecting a saved clip opens its editor; choose **← Your Clips** or the Clips navigation icon to return. Your source selector, Start/Stop Replay and Save Clip controls remain reachable at the top. In compact mode the button is shortened to **Save Clip**; the tooltip and configured global hotkey still indicate how many preceding seconds will be saved.

Flick uses the same graphite/violet styles across controls, with the full user-supplied sunset-squircle logo. It honors Windows High Contrast, reduced animations and system Transparency Effects where supported; transparent or animated effects fall back to simpler rendering when disabled. Use Tab/Shift+Tab to navigate, arrow keys for playback/trimming, and tooltips for truncated long window names. A visual inspection on the actual Windows PC at 100–200% display scale remains required for release.

### Tray & Capture Controls

The new compact capture bar is available at the top of the window. When replay is off, **Start Replay** is the primary action and **Save Last N Seconds** is disabled; when buffering or armed, **Save Last N Seconds** becomes the primary action. The same state is reflected in tray actions. The navigation rail contains **Your Clips** (focuses search or opens the library in compact mode) and **Settings** (opens the six-category preferences).

Changing capture source while recording asks for confirmation because it discards the current unsaved buffer. Declining retains the previous target. Closing Flick hides its window but does **not** stop replay; choose Stop Replay or Quit Flick explicitly.

### Export for Discord and File Sharing

The bottom export dock stays reachable while the editor scrolls. The file-size selector uses **decimal megabytes**: 20 MB means **20,000,000 bytes**, not 20 MiB. Use **10 MB** when the receiving Discord destination requires a smaller attachment. The upload limit may differ by destination or account; Flick never promises Discord will accept every exported cap.

While exporting, an indeterminate progress strip shows the real stage, not a fabricated countdown. **Cancel Export** interrupts the owned worker and discards incomplete temporary output. Selecting a different clip during export does not make that other clip Ready; the finished export belongs to its original selected clip.

The draggable tile represents the validated MP4 on disk, not a rendered screenshot, link, or virtual file. **Copy File** uses Windows' file-drop clipboard data. An advanced **Direct Posting** section remains collapsed; to send through a webhook you must explicitly enable it in Settings and separately confirm each send. Ordinary export never posts to Discord.

### Clip Editor and Non-Destructive Edits

The editor is a compact workflow: clip title, player, transport, a single trim timeline and collapsed **Export Audio** section. The Discord export dock remains accessible below the scrollable player and editing tools.

Trim handles support keyboard focus. The handles accept Left/Right (0.1s), Ctrl+Left/Right (1s), Home and End. Start and End fields accept decimal seconds, `M:SS.ff`, or `H:MM:SS.ff`, and reject invalid ranges. Each edit invalidates the previously marked-ready share file without changing the original. A selected 30s clip can be trimmed precisely to any valid segment of at least a quarter second.

Expanding **Export Audio** shows only actual tracks present in the recorded or imported source, not invented individual game/Discord channels. Mute is independent of gain. Reset Mix restores the appropriate conservative defaults and preserves the original. Audio edits are debounced for lightweight indexing while still invalidating a stale export immediately.

The player stops its decoding work when Flick is hidden to the tray or a new gameplay capture begins, to keep resource use low. A codec compatibility preview exists solely under Flick's local cache; it is not a new original or a Discord-ready export and is never uploaded.

## 3.1 Import Clips From Other Recording Apps

Click **+ Import Folder** in **Your Clips**, or **Settings → Connected Clip Folders → Manage Folders**. Select the folder containing videos from SteelSeries, OBS, NVIDIA or another local recorder. Confirm the folder link. Flick scans supported **MP4 and MKV** files and indexes them **in place**; the original file stays exactly where it was. Use **Include Subfolders** to include clips in game-specific subdirectories.

Import is manually initiated, cancelable and limited to 10,000 videos / eight directory levels per scan. A scan can be paused to prioritize replay capture, and completed entries stay indexed for later rescans. Large libraries load thumbnails on demand to avoid expensive simultaneous video decodes.

The library filters **All Clips**, **Flick** and **Imported** show the source, title, and duration of each video. If an external drive is disconnected, entries stay in Flick but appear **Offline** until the source returns. Use **Rescan** once reconnected; Flick does not continuously monitor external folders.

Right-click a clip for contextual options. Choosing **Remove** on an imported clip takes it out of Flick's library and excludes it from future rescans of that linked folder; its original video and exported files **are never deleted**. **Unlink Folder** removes only that folder's imported entries from Flick, not files on disk. By contrast, **Delete** on a Flick-recorded original retains its explicit Recycle Bin behavior.

**Saved Clips Folder** and **Export Folder** can be changed independently under Settings. A change applies to newly created files and never migrates or deletes existing media. No account, vendor integration or upload is required.

## 4. Limitations and troubleshooting

- **No video captured:** Confirm replay is manually armed, the correct target is selected, and the NVIDIA hardware encoder is available. Closing the selected window must not silently fall back to a monitor.
- **Clip is shorter than selected duration:** Replay duration is bounded by capture startup time, keyframe boundaries and the 256 MiB compressed buffer. Try Balanced or Light, or shorten the requested replay.
- **Audio sounds doubled:** Use the dedicated system/mic tracks for export; don't mix the combined preview track with its source tracks. Check headset monitoring if mic sound is already in system audio.
- **MKV playback fails:** Use **Create Local Preview** to make a temporary compatible MP4 **only when requested** (maximum first 180 seconds of a long imported recording, maximum 120 MB, and 90-second worker timeout). If a remuxed preview still fails, try **Try Compatible Preview** for an explicit reencode; otherwise use **Open in External Player**. Conversion does not run during active replay or exporting.
- **Audio mix preview differs from original:** Original playback is untouched. After editing, export a new MP4 and choose **Export Preview** to hear the actual share mix.
- **Editing controls disabled:** Trim and audio controls require an available clip; they are disabled during export or if the original is offline.
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
