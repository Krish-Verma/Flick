# LocalClip User Guide

LocalClip is a native, offline-first Windows 11 gameplay clipping application replicating the beloved SteelSeries Moments workflow: manual arming, instant 30-second replay saving to an immutable MKV file, non-destructive trimming, audio track adjustments, and guaranteed watermark-free export below Discord size limits (20 MB default).

---

## 1. System Requirements

- **Operating System:** Windows 10 / Windows 11 (64-bit)
- **Processor:** 4-core x64 CPU or better
- **Graphics Card:** NVIDIA GeForce GTX 900-series or newer (RTX 20/30/40/50-series recommended for high-efficiency NVENC)
- **Memory (RAM):** 8 GB minimum (16 GB recommended)
- **Storage:** 500 MB free for installation plus dedicated space for clips (default: `%USERPROFILE%\Videos\LocalClip`)
- **Audio:** Windows WASAPI audio device (speakers/headphones and optional microphone)

---

## 2. Installation and First Run

### Portable Mode (No Installation)
1. Extract `LocalClip-v1.0.0-win-x64.zip` to any folder (e.g. `D:\Tools\LocalClip` or `C:\LocalClip`).
2. Double-click `LocalClip.App.exe` to start.
3. Settings and clip indexes are stored locally in `%LOCALAPPDATA%\LocalClip`.

### Per-User Installation
1. Open PowerShell in the extracted folder.
2. Run:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```
3. This installs LocalClip into `%LOCALAPPDATA%\LocalClip` and creates shortcuts on your **Desktop** and **Start Menu**.

### Uninstallation
To remove LocalClip and its shortcuts:
```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\LocalClip\uninstall.ps1"
```
*(Your saved video clips in your Videos folder are never deleted during uninstallation.)*

---

## 3. Core Workflow

LocalClip strictly adheres to the principle that **capture starts DISARMED**. It will never secretly record your desktop or game without you explicitly arming it.

```
[Disarmed] --(Ctrl+Shift+F9)--> [Buffering (In-Memory 30s)] --(Ctrl+Shift+F8)--> [Saved MKV Original]
                                                                                        |
                                                                             [Review & Non-Destructive Trim]
                                                                                        |
                                                                             [Audio Balance / Mute]
                                                                                        |
                                                                             [Export for Discord (20 MB)]
                                                                                        |
                                                                      [Native Drag or Copy into Discord]
```

### Step 1: Arm Capture
- Launch LocalClip.
- Select your capture target from the dropdown:
  - **Monitors:** e.g., `Display 0: \\.\DISPLAY1 (1920x1080) [Primary]`
  - **Windows:** e.g., active game windows.
- Click **"ARM REPLAY"** or press **`Ctrl+Shift+F9`**.
- The status badge changes to **Buffering** (yellow/green). The capture host is now maintaining a rolling 30-second replay buffer in high-speed system memory (under 256 MiB). No video is written to disk yet.

### Step 2: Save a Replay Clip
- When an exciting gaming moment happens, press **`Ctrl+Shift+F8`** (or click **"SAVE REPLAY"**).
- LocalClip instantly flushes the preceding 30 seconds of video and multi-track audio to an immutable Matroska (`.mkv`) file in your clips folder:
  `%USERPROFILE%\Videos\LocalClip\Originals\Clip_YYYY-MM-DD_HH-mm-ss.mkv`
- A system tray notification confirms the clip was saved.

### Step 3: Review and Non-Destructive Trim
- Select the clip from the left-hand **Gallery**.
- Click the video preview or transport slider to inspect gameplay.
- Adjust the **Trim Start** and **Trim End** sliders:
  - Trim duration is shown in real-time.
  - Trimming is completely non-destructive: the original MKV file is never altered or truncated.

### Step 4: Multi-Track Audio Adjustment
- The **Audio Tracks** panel displays detected audio streams:
  - **Track 1:** Combined Mix / System Audio
  - **Track 2:** Microphone Input
- Adjust the **Gain** slider (0.0x to 2.0x) or check **Mute** for individual tracks (e.g. mute your mic if you only want game audio, or boost low microphone levels).
- An automatic audio limiter (`alimiter=limit=0.98`) prevents clipping and distortion.

### Step 5: Export for Discord
- Choose your Discord file size target:
  - **20 MB (Default - Free Discord limit)**
  - **50 MB**
  - **100 MB**
- Click **"Export for Discord"**.
- LocalClip automatically:
  1. Computes the optimal video/audio bitrate using a safety margin ($\ge \max(5\%, 256\text{ KiB})$).
  2. Encodes the trimmed segment using hardware NVENC to high-compatibility H.264/AAC MP4.
  3. Verifies that the resulting file does not exceed the target size cap. (If hardware encoder overshoots, bounded retry recalculates lower bitrate automatically).
  4. Runs a full decode verification to ensure zero stream corruption.
  5. Atomically saves the verified export to:
     `%USERPROFILE%\Videos\LocalClip\Exports\<ClipName>_Discord_20MB.mp4`

### Step 6: Native Discord Sharing
Once the export is ready:
- **Option A (Drag & Drop):** Click and drag the **"DRAG TO DISCORD"** button directly into any Discord channel or DM text box.
- **Option B (Copy File):** Click **"Copy File"** and press **`Ctrl+V`** in Discord.
- **Option C (Open Folder):** Click **"Open Folder"** to view the exported file in Windows File Explorer.

---

## 4. Optional Discord Webhook Posting

For communities or private clip channels:
1. Open **Settings** (`Ctrl+,` or click "Settings").
2. Check **Enable Direct Discord Webhook Posting**.
3. Paste a valid Discord webhook URL (`https://discord.com/api/webhooks/<ID>/<TOKEN>`).
4. Enter the channel name for visual verification.
5. Click **Save Settings**.
6. The webhook secret is encrypted on disk using Windows DPAPI (`DataProtectionScope.CurrentUser`). It is never stored in plain text.
7. To post, click **"Post to Webhook"** on any ready export. A confirmation dialog will prompt you before sending.

---

## 5. Keyboard Shortcuts

| Shortcut | Action | Description |
|---|---|---|
| **`Ctrl+Shift+F9`** | Toggle Capture | Arms or disarms the rolling replay buffer |
| **`Ctrl+Shift+F8`** | Save Replay | Saves the preceding 30 seconds to MKV |
| **`Space`** | Play / Pause | Toggles playback in the clip viewer |

Shortcuts can be customized in `%LOCALAPPDATA%\LocalClip\settings.json`.

---

## 6. Technical Limitations & Troubleshooting

- **Elevated Windows (UIPI):** In Windows, non-administrator apps cannot capture windows belonging to elevated (Run as Administrator) processes due to User Interface Privilege Isolation (UIPI). If your game runs with elevated administrator rights, launch `LocalClip.App.exe` as Administrator or select Display/Monitor capture.
- **Single Instance:** Only one instance of LocalClip can run at a time to prevent port/hotkey conflicts and GPU encoder resource contention. If you try to launch it again, the existing instance will be brought to focus.
- **Audio Separation Fallback:** When per-process WASAPI audio capture is unavailable, LocalClip automatically routes "System Audio Loopback + Microphone" and labels it as fallback. It never fakes isolation.
- **Hardware Encoder:** LocalClip prioritizes NVIDIA NVENC (`obs_nvenc_h264_tex`). If your GPU is under 100% compute load, ensure your GPU driver is up to date. Software x264 fallback is supported if hardware encoding is unavailable.
