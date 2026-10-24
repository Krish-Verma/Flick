# Flick — User Guide (Development Build)

> Flick v1.0.6-preview.1 is a publicly downloadable, **unsigned** developer preview. GitHub Latest identifies the newest preview, not trusted publisher status or completed physical Windows/GPU qualification. Source CI does not replace the owner's hardware acceptance.

Flick is a small, free, offline-first **Windows 11 x64** gameplay clipper. Save recent action, organize and trim clips, adjust the audio in a share copy, and prepare an MP4 without a watermark. Recording stays manual unless you deliberately enable automatic replay for approved games.

This guide includes the current development workflows. Your installed preview may not contain every feature described here. Implemented features, automated checks and real-world hardware verification are separate; the development changes are not a new public release.

## 1. Install or run

Two preview formats are available after a verified Windows build: extract `Flick-v1.0.6-win-x64.zip` and launch **Flick.exe**, or run the unsigned `Flick-Setup.exe` wizard for a normal **Start Menu** entry, optional desktop shortcut and Windows **Installed Apps** uninstall registration. The Inno Setup build is publicly available as an unsigned preview, with full physical hardware and Smart App Control qualification still outstanding. **Do not run the previous `LocalClip` `uninstall.ps1`**, which could erase the directory containing user metadata. New program files install in `%LOCALAPPDATA%\Programs\Flick`, separately from settings and clips.

Flick stores settings and metadata under `%LOCALAPPDATA%\LocalClip`; originals are saved by default to `%USERPROFILE%\Videos\LocalClip\Originals`. You may change the originals folder in Settings.

### Uninstalling the normal Windows app

Choose **Settings > General > Uninstall Flick**, or open **Windows Settings > Apps > Installed Apps > Flick > Uninstall**. The installed Windows uninstaller optionally removes local settings and cache only after a separate confirmation; original recordings, exports and imported sources are never deleted. Portable ZIP copies should be closed and the extracted folder deleted manually. The downloaded Setup EXE remains in Downloads until you delete it.

Compatibility: Windows 11 x64 with working D3D11 graphics is the target. NVIDIA NVENC, AMD AMF, Intel Quick Sync and optional x264 CPU fallback are implemented but require physical exact-machine testing in v1.0.6. Windows ARM, 32-bit Windows and Windows 10 are not qualified.

## 2. The everyday clipping workflow

1. Open Flick or restore it from its tray icon. **Replay is off by default**, including at Windows sign-in. Selecting a source or minimizing Flick does not start it. If you explicitly enabled automatic game replay, an eligible approved game may start replay while Flick is open or in the tray; see **Games** below.
2. Choose a specific monitor or game window in the target dropdown. Click **Start Replay**, or press **Ctrl+Shift+F9** (configurable).
3. Play normally while Flick fills a bounded replay buffer. The UI displays an **estimated** buffered duration; a 256 MiB compressed packet limit can shorten a high-bitrate, long clip.
4. When you want to capture a moment, press **Ctrl+Shift+F8**, or click **Save Last 30 Seconds** (or your configured duration). Flick saves the recent replay to an MKV original with a unique filename; subsequent saves do not intentionally replace old originals. Saving does **not** stop ongoing replay capture.
5. Select your saved recording in **Your Clips**. The player first displays a lightweight thumbnail **poster**, without loading its video decoder. Press **Play** to open the Original. Use **Play/Pause**, the seek slider or its arrow keys, and the small **Original / Export Preview** dropdown. If Windows cannot decode the MKV, click **Create Local Preview** for an explicitly requested, bounded H.264 MP4, or **Open in External Player**. Originals are never changed by preview generation.
6. Open **Trim** to adjust the two handles or enter exact Start/End times. Use **Set In**, **Set Out**, **Undo trim**, **Add marker**, or **Reset** as needed. The smallest selection is 0.25 seconds. Open **Audio** for the tracks actually present in the clip, mute/gain controls, **Preview Mix** and **Reset Mix**. Check the saved/error message after editing; originals stay unchanged.
7. Open **Share**, choose the **20 MB default**, **10/50/100 MB** or **Custom 1–250 MB** size limit, and choose **Original aspect**, **Fit 9:16** or **Fit 1:1**. Click **Export Clip**. The activity strip and status show progress; **Details** keeps the full current message available. **Cancel Export** stops the work and clears incomplete output while keeping the original. Flick checks the finished MP4 before marking it ready.
8. Choose **Export Preview** to review the actual finished picture and audio. Once the tile says **MP4 ready**, drag the tile into Discord or use **Copy File** and paste; **Open Folder** reveals the file. The tile’s still image is an **original thumbnail**, not a preview of the final trim or fitted canvas. Check Discord’s attachment preview before sending. Editing trim, audio, shape or size may require a new export. **Flick does not automatically send a message.**
9. Click **Stop Replay** to release the encoder, or quit Flick through the tray menu to stop all owned helpers. Closing the window hides it to the tray; minimizing is not the same as stopping capture.

## 3. Small, useful settings

Open **Settings** from the navigation rail or tray. It has seven categories: **Recording, Games, Audio, Shortcuts, Storage, General, Sharing**. Only the selected category is visible. **Opening Settings does not stop recording.** Quality and duration changes apply the next time you manually start replay. Disabling an actively recording microphone requires explicit confirmation because it stops the current buffer to release the device.

| Setting | Options and behavior |
|---|---|
| Replay duration | 15–120 seconds; default **30s** |
| Recording quality | **Light** (720p/30 FPS, 6 Mbps), **Balanced** (1080p/30 FPS, 15 Mbps), **Smooth** (1080p/60 FPS, 20 Mbps) |
| Memory budget | Compressed replay packet budget 256 MiB; settings warn if a profile is likely to shorten the requested replay |
| Automatic game replay | Off by default; approve running games and explicitly enable their profiles |
| Microphone | Off by default; choose Record Microphone explicitly. Automatic replay also needs that game’s microphone permission |
| Capture audio | System Audio records the whole chosen output device; Separate App Audio records selected Game/Voice process trees where supported |
| Hotkeys | Separate customizable Capture Toggle and Save Clip keys; incompatible or conflicting registrations show errors |
| Saved Clips Folder | Choose where future replay MKVs are written; existing originals are not moved |
| Export Folder | Choose a separate location for future Discord-ready MP4s; existing exports are not moved |
| Connected Clip Folders | Link existing local MP4/MKV folders in place; nothing is uploaded or copied |
| Windows startup | Optional launch at sign-in; manual replay stays off. Explicitly enabled automatic game replay may act on an approved game |
| Open in tray | Optional hidden launch with tray controls, separate from capture status |
| Clip Saved Overlay | Enabled by default; temporary click-through **Clip Saved** banner with actual duration after a validated save. No title, filename or clip ID |
| Confirmation Sound | Enabled by default, independently toggleable; quiet, short in-process chime (may be heard by system-loopback capture) |
| Preview Notification | A button to see/hear sample feedback without saving preferences or starting capture |
| Discord sharing | 10/20/50/100 MB and Custom cap; 20 MB default, optional encrypted direct webhook disabled by default and requiring explicit per-send confirmation |

Long/high-quality replays may exceed the compressed packet budget and retain less history. A video’s settings describe the requested profile, not a guarantee of actual hardware FPS or image quality. Starting with 1.0.5, Flick offers an optional CPU x264 fallback (720p/30 maximum) when no hardware encoder starts. The default enables this fallback and clearly reports the actual selected encoder; disable it under Recording if you prefer recording to fail rather than consume CPU.

**Restore Defaults** stages the factory preferences; press **Save Settings** to apply them. **Cancel** leaves saved preferences unchanged. Shortcut fields accept actual key chords, and each setting category uses Flick's shared typography and control sizes. Existing originals are never deleted by Restore or Cancel.

### Clip Saved Feedback

By default, a successfully validated and indexed clip triggers **Clip Saved** and its actual duration in a temporary banner, alongside the Flick logo and a short sound. The success banner does not show the clip title, filename or ID. The banner and sound are **independent** settings. Turning the banner off does not disable the sound; turning the sound off does not disable the banner. A failed save shows a different failure notification and never plays the success chime.

The notification is temporary and creates no permanent game overlay process. Native topmost WPF windows may not appear above every exclusive-fullscreen game; this behavior, mixed-DPI placement, and FPS impact still need validation on the owner's Windows PC. The banner should not interrupt gameplay focus.

### Window Layout, Scaling and Accessibility

The footer shows **RAM** and **CPU**, refreshing about once a second while Flick is open, including when replay is off. Readers run in the background independently of recording-status requests. Hover over the readings for the **Flick** and **Recording** breakdown. In **Settings → General → Display**, turn **Show RAM and CPU** off to hide the figures and stop their optional sampling. Save applies the change; Cancel keeps the previous preference. These figures cover Flick and its current recording helper; separate export tools and the private recording-test helper are excluded. An unavailable reading is shown as **--** rather than zero.

At normal desktop widths (1120+ logical WPF pixels), Flick shows **Your Clips** on the left and the editor on the right. On narrower or highly scaled displays, Flick switches to a **compact one-pane layout**. Selecting a saved clip opens its editor; choose **← Your Clips** or the Clips navigation icon to return. Your source selector, Start/Stop Replay and Save Clip controls remain reachable at the top. In compact mode the button is shortened to **Save Clip**; the tooltip and configured global hotkey still indicate how many preceding seconds will be saved.

Flick uses the same graphite/violet styles across controls, with the full user-supplied sunset-squircle logo. It honors Windows High Contrast, reduced animations and system Transparency Effects where supported; transparent or animated effects fall back to simpler rendering when disabled. Use Tab/Shift+Tab to navigate, arrow keys for playback/trimming, and tooltips for truncated long window names. A visual inspection on the actual Windows PC at 100–200% display scale remains required for release.

### Tray & Capture Controls

The new compact capture bar is available at the top of the window. When replay is off, **Start Replay** is the primary action and **Save Last N Seconds** is disabled; while Starting or Buffering, Save stays disabled; once **Replay Ready**, **Save Last N Seconds** is available. The same state is reflected in tray actions. The navigation rail contains **Your Clips** (focuses search or opens the library in compact mode) and **Settings** (opens preferences).

Changing capture source while recording asks for confirmation because it discards the current unsaved buffer. Declining retains the previous target. Closing Flick hides its window but does **not** stop replay; choose Stop Replay or Quit Flick explicitly.

### Export for Discord and File Sharing

In Share, the preview, size and shape choices, recent exports, status and actions fit in one panel without scrolling, including the minimum window size. **Details** opens the complete export message and format explanation. **Export Clip**, **Copy File**, **Open Folder** and **Cancel Export** remain visible. The file-size selector uses **decimal megabytes**: 20 MB means **20,000,000 bytes**, not 20 MiB. Use **10 MB** when the receiving Discord destination requires a smaller attachment. The upload limit may differ by destination or account; Flick never promises Discord will accept every exported cap.

Choose **Recent exports**, when available, to reopen one of the newest valid prepared files and its size limit without changing your saved default. The picker keeps up to 20 entries and rechecks availability; it disappears when none qualify. Fit 9:16 and Fit 1:1 place the full picture inside a black canvas without cropping. Resolution may reduce to meet the chosen size cap.

While exporting, an indeterminate progress strip shows the real stage, not a fabricated countdown. **Cancel Export** interrupts the owned worker and discards incomplete temporary output. Selecting a different clip during export does not make that other clip Ready; the finished export belongs to its original selected clip.

The draggable tile hands Windows the actual prepared MP4 file on disk. Its displayed original thumbnail is a visual aid; it is not the file being transferred. **Export Preview** plays the finished MP4, including its trim, mix and frame shape. **Copy File** uses Windows' file-drop clipboard data. **More → Send to Channel…** is optional direct posting; to use a webhook you must explicitly enable it in Settings and separately confirm each send. Ordinary export never posts to Discord.

### Clip Editor and Non-Destructive Edits

Choose **Trim**, **Audio** or **Share** below the player. Each opens a compact drawer inside the editor. Close it, or press Escape, to return to the player. Opening Share only shows the available actions; it does not export or upload anything.

Trim handles support keyboard focus. The handles accept Left/Right (0.1s), Ctrl+Left/Right (1s), Home and End. Start and End fields accept decimal seconds, `M:SS.ff`, or `H:MM:SS.ff`, and reject invalid ranges. Each edit invalidates the previously marked-ready share file without changing the original. A selected 30s clip can be trimmed precisely to any valid segment of at least a quarter second. **Undo trim** keeps up to 20 changes for the current clip and clears when you switch clips. Invalid time entry keeps the previous range; failed saves show an error and restore it.

When the Trim drawer has focus outside typing fields and other editing controls:

| Shortcut | Action |
|---|---|
| Left / Right | Seek one second |
| Ctrl+Left / Ctrl+Right | Seek five seconds |
| Home / End | Seek to the original’s beginning/end |
| I / O | Set the trim start/end at the playhead |
| M | Add a marker |
| Ctrl+Z | Undo the last trim |

Click the timeline or a marker to seek. Markers are your own bookmarks, not automatic game-event detection. You can keep up to 32 per clip; right-click one to remove it. They remain relative to the original recording.

For an optional visual guide, expand **Preview frames** and choose **Load six frames** while replay and other media work are off. Click a frame to seek. Frames load only when requested and are cancelled when you switch clips, close the drawer, hide Flick or start recording/exporting. A missing thumbnail never prevents normal timeline navigation. The small images have a fixed memory limit; the displayed memory observation is not a promise about game performance.

Expanding **Export Audio** shows only actual tracks present in the recorded or imported source, not invented individual game/Discord channels. Mute is independent of gain. **Combined** is an alternate complete mix: selecting it excludes its dedicated source tracks, so the same audio is not mixed twice. **Reset Mix** selects the appropriate dedicated tracks when they are known, or the first audio track for an ordinary import. **Preview Mix** creates a temporary copy using the chosen trim and audio levels. It does not change the original; all-muted produces silence. Audio edits are debounced for lightweight indexing while still invalidating a stale export immediately.

The player stops its decoding work when Flick is hidden to the tray or a new gameplay capture begins, to keep resource use low. A codec compatibility preview exists solely under Flick's local cache; it is not a new original or a Discord-ready export and is never uploaded.

## 3.1 Import Clips From Other Recording Apps

Click **+ Import Folder** in **Your Clips**, or **Settings → Storage → Manage Folders**. Select the folder containing videos from SteelSeries, OBS, NVIDIA or another local recorder. Confirm the folder link. Flick scans supported **MP4 and MKV** files and indexes them **in place**; the original file stays exactly where it was. Use **Include Subfolders** to include clips in game-specific subdirectories.

Import is manually initiated, cancelable and limited to 10,000 videos / eight directory levels per scan. A scan can be paused to prioritize replay capture, and completed entries stay indexed for later rescans. Large libraries load thumbnails on demand to avoid expensive simultaneous video decodes.

The library filters **All Clips**, **Flick** and **Imported** show where each video came from. Open **Clip details** to edit its game label and comma-separated tags, then choose **Save details**. Tags ignore case and duplicates; use up to 12 tags of 32 characters each and a game label of up to 80 characters. **Favorite** saves immediately. Unsaved details prompt before you change clips or quit.

Search matches title, tags, game, source and date (`yyyy-mm-dd`); `#clutch` also matches a tag. Combine **Favorites only**, a game and an optional 7/30-day date filter. Sort by newest, oldest, game, title or **Recently Exported**. Game sorting adds group headings. Favorites, tags and markers stay local and do not require a new export. If an external drive is disconnected, entries stay in Flick but appear **Offline** until the source returns. Use **Rescan** once reconnected; Flick does not continuously monitor external folders.

Right-click a clip for contextual options. Choosing **Remove** on an imported clip takes it out of Flick's library and excludes it from future rescans of that linked folder; its original video and exported files **are never deleted**. **Unlink Folder** removes only that folder's imported entries from Flick, not files on disk. By contrast, **Delete** on a Flick-recorded original retains its explicit Recycle Bin behavior.

**Saved Clips Folder** and **Export Folder** can be changed independently under Settings. A change applies to newly created files and never migrates or deletes existing media. No account, vendor integration or upload is required.

## 3.2 Optional automatic game replay

Automatic replay is **off by default**. To enable it:

1. Open the game, then **Settings → Games → Find Running Windows**.
2. Select its window and choose **Approve Selected Game**. Review the displayed program location; a matching name alone is not approval.
3. Set that profile’s replay quality and duration. Leave **Allow Microphone for This Game** off unless wanted. Profile edits are staged until **Save Settings**.
4. Enable **Automatic Game Replay**, save and review the confirmation. You can keep up to 32 approved profiles.

With this enabled, Flick checks for eligible approved windows about every three seconds. Exactly one unambiguous game window can start replay. Flick does not choose an unrelated app or fall back to recording the desktop. The capture bar names the automatic game. Your manual source and recording preferences remain separate.

**Stop Replay pauses that running game session**, including if Flick restarts. A minimized, lost or failed game capture is also paused rather than silently retried. Restore/minimize behavior is conservative: restart the game or start replay manually when you want to record again. Alt+Tab can keep an otherwise visible game recording. Multiple eligible games/windows, protected or elevated games, and uncertain identities prevent automatic recording.

Disable automatic replay or remove a profile under Games when no longer wanted. Saving changes to active automatic settings asks before stopping that replay and losing its unsaved buffer; saved clips remain. Cancel leaves the saved configuration alone.

Separate App Audio has its own process selection. After a Game or Voice app restarts, refresh its audio selection before expecting automatic replay with separated audio to work. Flick refuses a stale audio source rather than switching to whole-device sound. This development feature still needs real game, display and hardware acceptance; approval is not a claim of universal game or anti-cheat compatibility.

## 3.3 Choose what audio to record

Under **Settings → Audio**, choose one mode:

- **System Audio · whole output device** records everything playing on the configured output device. This can include voice chat, music and notification sounds.
- **Separate App Audio · selected process trees** lets you refresh running apps and explicitly choose Game and/or Voice. Each choice includes that app and its child processes; “Voice” is a role you assign, not recognition of individual people. Choosing the same or overlapping app trees is refused.

Separate App Audio requires a supported Windows version (build 20348 or newer) and successful access to the chosen apps. **Refresh Running Apps and reselect after an app restarts.** If an app is unavailable or capture cannot attach, replay refuses or stops; there is no silent fallback to whole-device sound. If sound comes from a sibling process outside your selection, it may be silent. Choose System Audio yourself if that is the fallback you want.

**Record Microphone** is independent and off by default. Automatic replay also needs **Allow Microphone for This Game**. Audio and microphone changes stay staged until **Save Settings**. Saving a change during active replay asks before stopping its unsaved buffer; **Cancel** leaves the saved choices alone. To stop recording immediately, use **Stop Replay**.

Recording Health reports the actual routing and whether an app source attached or sound was observed. These labels do not prove that every desired sound was isolated. Headphones/speakers, output changes, Discord’s process layout and microphone behavior require checks on your own setup. Play a saved clip’s tracks and final export before relying on a new routing arrangement.

## 3.4 Recording Health and private diagnostics

Click the recording status at the top to open **Recording Health**. It shows the selected source, actual encoder, resolution/frame rate, estimated retained replay duration, helper health, shortcut registration and recent errors or saved clips. **Replay Ready** means there is replay data ready to save; it does not promise the full requested duration is retained.

During saving, **Saving Clip** includes file validation and library registration. A save acknowledgement alone is not success. **Save Successful** and the success banner require a validated saved file. **Save Failed**, **Recording Faulted** and **Recording Status Unavailable** remain distinguishable; a slow or unavailable status check is not treated as proof recording stopped. Recent errors remain available after the recording helper exits.

Choose **Run Recording Test** for a short, explicit test of the selected source. Read the source/audio confirmation first. **Include configured audio in test** is off by default; with it off, no system or microphone source is captured. The test saves a clip in Your Clips and releases its separate recording helper. Review its result and, when useful, play the retained clip. **Cancel Test** stops the test and waits for cleanup; any already saved original stays kept. One successful test does not qualify every game, GPU or audio device.

**Export Diagnostics** lets you choose where to save a local troubleshooting ZIP. The small local history is bounded to 256 events, seven days and 256 KiB. The package includes safe state/category and numeric details, not recordings, titles, file paths, clip IDs, webhook credentials or raw error text. It is not sent anywhere automatically. Local error details shown in the app can contain a path or source name; that does not put them into the exported package. Review any other material you separately choose to share.

## 4. Limitations and troubleshooting

- **No video captured:** Replay is off by default. Select the correct source, press **Start Replay** or **Ctrl+Shift+F9**, wait for **Replay Ready**, and only then press **Save Clip** or **Ctrl+Shift+F8**. Open Recording Health for missing runtime, graphics/encoder, target or file-save errors. The configured hotkeys may differ; Windows can reserve conflicting shortcuts. Closing the selected window stops/refuses capture rather than switching to a monitor.
- **Clip is shorter than selected duration:** Replay duration is bounded by capture startup time, keyframe boundaries and the 256 MiB compressed buffer. Try Balanced or Light, or shorten the requested replay.
- **Audio sounds doubled:** Use the dedicated tracks or Combined as alternatives. Check headset monitoring if mic sound is already present in captured system/game audio; separate tracks cannot remove duplication already present at the source.
- **MKV playback fails:** Use **Create Local Preview** to make a temporary compatible MP4 **only when requested** (maximum first 180 seconds of a long imported recording, maximum 120 MB, and 90-second worker timeout). If a remuxed preview still fails, try **Try Compatible Preview** for an explicit reencode; otherwise use **Open in External Player**. Conversion does not run during active replay or exporting.
- **Audio mix differs from original playback:** Original playback is untouched. Use **Preview Mix** to hear the chosen edit, or export and choose **Export Preview** to review the actual share file. Refresh app-audio selections if those apps restarted.
- **Editing controls disabled:** Trim and audio controls require an available clip; they are disabled during export or if the original is offline.
- **Hotkey unavailable:** Choose a different combination in Settings; Windows may reserve shortcuts used by another program.
- **Export unavailable:** Review the file cap, selected trim, storage space and encoder. Invalid exports must not be marked Ready.
- **Discord attachment is refused:** Use the prepared MP4 and try Copy File if drag does not attach it. Choose a smaller file-size cap if the destination allows less. Real Discord acceptance depends on your desktop/browser and destination; this development guide does not certify that flow on every setup.
- **Automatic replay did not restart:** Stop, minimize, source loss or an earlier failure pauses that game process. Start manually or restart the game. Check for multiple eligible games, fresh app-audio selections and messages in Games/Recording Health.
- **Startup trouble:** Disable Windows startup in Settings. Launch-at-sign-in and automatic game replay are separate opt-ins; review Games if you want all recording to remain manual.
- **Performance:** Compare game FPS with replay Off and On on the same scene; record App/Host memory and GPU encoder use. Do not change unrelated PC services/drivers merely to improve numbers.

## 5. Verification for contributors

Read `docs/product-phase/MASTER-PLAN.md` and `docs/product-phase/EVIDENCE.md` for the current implementation and acceptance record. Authored tests, builds, rendered fixtures, physical capture and publication are separate gates. Do not treat a skipped hardware case or an attractive screenshot as a passing real-world test.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_tests.ps1
powershell -ExecutionPolicy Bypass -File scripts/run_tests.ps1 -Hardware
```

The second command requires an authorized Windows hardware setup and may exercise recording. Review its source/audio requirements first. Missing required hardware must **fail**, not silently pass. `scripts/check_motion.ps1` samples video frames for motion but does not replace watching real gameplay, listening to each track and testing the full installed app and Discord drag/paste.

See `LICENSE` and `THIRD-PARTY-NOTICES.md` for Flick’s license and bundled software notices. Development changes do not establish signing, universal hardware compatibility, completed Discord acceptance or permission to publish a new release.
