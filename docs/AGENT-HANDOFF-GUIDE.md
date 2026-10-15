# Agent Handoff & Codebase Inspection Guide

**Target Audience:** Autonomous Coding Agents & Human Auditors  
**Project:** LocalClip (Offline-First Windows 11 Gameplay Clipper)  
**Primary Language/Frameworks:** C# (.NET 10 / WPF), C++17 (libobs, MinGW-w64 UCRT), FFmpeg  
**Target Hardware:** Windows 11 x64, NVIDIA GeForce RTX GPU (tested on RTX 5060, Driver 617.42)

---

## 1. Executive Summary for Incoming Agents

LocalClip was developed to provide an uncompromised, 100% free, offline-first implementation of the SteelSeries Moments clipping workflow on Windows 11. It intentionally rejects social feeds, telemetry, accounts, paywalls, and auto-clipping.

### Key Invariants (Do Not Break)
1. **Manual Arming Invariant:** The capture engine starts **DISARMED** on application startup. Auto-arming, hidden background capture, and background disk writing before an explicit user hotkey are strictly prohibited. Enforced in `AppSettings.cs` via `[JsonIgnore] public bool StartArmed => false;`.
2. **Immutability Invariant:** Original recordings flushed from the in-memory buffer to `Originals/*.mkv` are immutable. Trimming, audio mixing, and Discord export generate new files; the original MKV is never altered or truncated.
3. **Budget Invariant:** Exported MP4 files must never exceed the target byte limit (20 MB = 20,000,000 bytes by default). If hardware NVENC overshoots, bounded retry recalculates with bitrate penalties.
4. **Worker Confinement Invariant:** Subprocesses (`LocalClip.CaptureHost.exe`) must always be attached to a Windows Job Object configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Even if the parent crashes, zero orphan processes are left running.
5. **Security Invariant:** Webhook secrets are protected via Windows DPAPI (`DataProtectionScope.CurrentUser`). Payloads explicitly enforce `allowed_mentions: { parse: [] }`.

---

## 2. Directory Structure & File Inventory

### `src/LocalClip.Core/` (Business Logic & Pipeline)
- **`Models/`**
  - [`Settings.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Models/Settings.cs): User configuration with atomic JSON serialization (`.tmp` write and overwrite).
  - [`ClipInfo.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Models/ClipInfo.cs): `ClipMetadata` and `AudioTrackInfo` data models.
  - [`TargetInfo.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Models/TargetInfo.cs): Monitor and window capture target descriptors.
- **`Rules/`**
  - [`BitrateBudgetCalculator.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Rules/BitrateBudgetCalculator.cs): Strict distinction between decimal MB ($1,000,000\text{ B}$) and binary MiB ($1,048,576\text{ B}$), safety margin ($\ge \max(5\%, 256\text{ KiB})$), downscaling recommendations, and retry overshoot penalties.
  - [`TrimValidator.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Rules/TrimValidator.cs): Enforces non-destructive boundary rules ($0 \le \text{start} < \text{end} \le \text{duration}$, minimum 0.25s).
  - [`ExportFingerprint.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Rules/ExportFingerprint.cs): SHA-256 cache invalidation hashing source file write time, trim boundaries, active tracks, gains, and target size.
- **`Process/`**
  - [`JobObject.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Process/JobObject.cs): Windows Job Object wrapper ensuring child processes terminate if the host closes or crashes.
- **`Ipc/`**
  - [`CaptureHostProtocol.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Ipc/CaptureHostProtocol.cs): Request, response, and event data structures.
  - [`CaptureHostClient.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Ipc/CaptureHostClient.cs): Asynchronous stdio JSON IPC coordinator with background reader threads and request ID matching.
- **`Media/`**
  - [`MediaProber.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Media/MediaProber.cs): FFprobe JSON parser extracting resolution, FPS, duration, and audio track layouts; generates video thumbnails via FFmpeg.
  - [`ClipExporter.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Media/ClipExporter.cs): Discord MP4 export coordinator using hardware NVENC, multi-track audio downmixing filtergraph (`amix` + `alimiter`), size validation, bounded retries (up to 3), and atomic file promotion.
- **`Storage/`**
  - [`ClipStorage.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Storage/ClipStorage.cs): Thread-safe clip index manager with directory rescan, atomic index saving, and Windows Recycle Bin deletion (`SHFileOperation`).
- **`Diagnostics/`**
  - [`ResourceMonitor.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Diagnostics/ResourceMonitor.cs): Samples working set, private bytes, handle count, CPU%, and enforces physical memory safety guards (<1 GiB).
- **`Sharing/`**
  - [`DiscordWebhook.cs`](file:///d:/Documents/ClipMax/src/LocalClip.Core/Sharing/DiscordWebhook.cs): Optional webhook poster with DPAPI encryption, regex validation, HTTP 429 rate limit parsing, and error recovery.

### `src/LocalClip.CaptureHost/` (Native C++ libobs Engine)
- [`main.cpp`](file:///d:/Documents/ClipMax/src/LocalClip.CaptureHost/main.cpp):
  - Stdio JSON event-driven command listener (`ping`, `enum_targets`, `start_capture`, `save_replay`, `get_status`, `stop_capture`, `shutdown`).
  - Initializes libobs D3D11 graphics subsystem and audio subsystem.
  - Dynamically loads approved modules: `obs-nvenc`, `obs-ffmpeg`, `obs-outputs`, `win-capture`, `win-wasapi`.
  - Configures circular memory buffer (`replay_buffer`) bounded to 256 MiB.
  - Routes multi-track audio (System Audio on Track 1, Mic on Track 2).
  - Handles window enumeration and cloaked window filtering (`DWMWA_CLOAKED`).
  - Statically links runtime libraries (`-static-libgcc -static-libstdc++`) to avoid missing MinGW DLLs.

### `src/LocalClip.App/` (WPF Frontend UI)
- [`App.xaml.cs`](file:///d:/Documents/ClipMax/src/LocalClip.App/App.xaml.cs): User-scoped named mutex (`LocalClip_SingleInstance_UserLock`) enforcing single instance.
- [`MainWindow.xaml`](file:///d:/Documents/ClipMax/src/LocalClip.App/MainWindow.xaml) & [`.xaml.cs`](file:///d:/Documents/ClipMax/src/LocalClip.App/MainWindow.xaml.cs):
  - Capture controls (target selector, arm/disarm button, save button, buffer status).
  - Gallery list with thumbnails, creation timestamps, and target labels.
  - Video player review using `MediaElement` with transport slider.
  - Range sliders for non-destructive trim.
  - Track list with mute checkboxes and gain sliders.
  - Discord export presets (20 MB default, 50 MB, 100 MB).
  - Native drag handle (`DragDrop.DoDragDrop`) and "Copy File" button (`Clipboard.SetFileDropList`) for direct Discord desktop pasting.
- [`Hotkeys/HotkeyManager.cs`](file:///d:/Documents/ClipMax/src/LocalClip.App/Hotkeys/HotkeyManager.cs): Win32 `RegisterHotKey` listener with conflict detection.
- [`ViewModels/MainViewModel.cs`](file:///d:/Documents/ClipMax/src/LocalClip.App/ViewModels/MainViewModel.cs): MVVM coordinator binding UI to `CaptureHostClient`, `ClipStorage`, and `ClipExporter`.

### `tests/LocalClip.Core.Tests/` (Automated Test Suite)
- [`CoreTests.cs`](file:///d:/Documents/ClipMax/tests/LocalClip.Core.Tests/CoreTests.cs): 19 unit tests for bitrate math, margins, trim validation, fingerprints, DPAPI, and URL regex.
- [`WebhookMockTests.cs`](file:///d:/Documents/ClipMax/tests/LocalClip.Core.Tests/WebhookMockTests.cs): 5 mock tests for webhook response parsing (200, 429, 401/403/404/413, cancel, missing file).
- [`AudioFilterGraphTests.cs`](file:///d:/Documents/ClipMax/tests/LocalClip.Core.Tests/AudioFilterGraphTests.cs): 4 tests for FFmpeg audio mix filtergraph generation.
- [`StorageAndRecoveryTests.cs`](file:///d:/Documents/ClipMax/tests/LocalClip.Core.Tests/StorageAndRecoveryTests.cs): 3 tests for defaults, corrupted JSON recovery, and Unicode/emoji filenames.
- [`ResourceMonitorTests.cs`](file:///d:/Documents/ClipMax/tests/LocalClip.Core.Tests/ResourceMonitorTests.cs): 2 tests for process sampling and RAM guards.
- [`IntegrationTests.cs`](file:///d:/Documents/ClipMax/tests/LocalClip.Core.Tests/IntegrationTests.cs): 2 live end-to-end capture, replay save, and export tests on real hardware.
- [`LifecycleStressTests.cs`](file:///d:/Documents/ClipMax/tests/LocalClip.Core.Tests/LifecycleStressTests.cs): 4 tests for 50 start/stop cycles, 20 target switches, disarmed save rejection, and crash cleanup.

---

## 3. How to Inspect and Verify as an Agent

### Step 1: Run the Full Test Suite
Execute the automated test suite runner:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\run_tests.ps1
```
*Verification:* Expect 41 tests passed, 0 failed, 0 skipped.

### Step 2: Verify the Release Build & Package
Rebuild the release artifacts:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build_package.ps1
```
*Verification:*
- Inspect `dist/SHA256SUMS.txt` and ensure `dist/LocalClip-v1.0.0-win-x64.zip` is generated.
- Check that `dist/app/LocalClip.App.exe` and `tools/obs/runtime/bin/64bit/LocalClip.CaptureHost.exe` compile cleanly with 0 errors.

### Step 3: Verify the Running Application
Launch the compiled application:
```powershell
$proc = Start-Process -FilePath "dist\app\LocalClip.App.exe" -PassThru
Start-Sleep -Seconds 3
Get-Process -Id $proc.Id
Stop-Process -Id $proc.Id -Force
```
*Verification:*
- Ensure Working Set is low (< 50 MB idle).
- Ensure no orphan `LocalClip.CaptureHost.exe` processes remain after stopping the app.

---

## 4. Notable Implementation Nuances & Gotchas Resolved

1. **WPF and WinForms Namespace Clashes:**
   - In `LocalClip.App.csproj`, `<UseWindowsForms>true</UseWindowsForms>` is enabled for `NotifyIcon`, but `<Using Remove="System.Windows.Forms" />` is configured to prevent global namespace pollution. WPF namespaces (`System.Windows`, `System.Windows.Media`, `System.Windows.Controls`) remain clean.
2. **MinGW Static Runtime Requirement:**
   - Native C++ builds must specify `-static-libgcc -static-libstdc++` and `"-Wl,-Bstatic" -lwinpthread "-Wl,-Bdynamic" -lobs -ldwmapi`. Without this, Windows will fail to launch `CaptureHost.exe` with error `0xC0000135` (`STATUS_DLL_NOT_FOUND`) due to missing MinGW runtime DLLs in the runtime bin directory.
3. **OBS Replay Buffer Filename Lookup:**
   - The `replay_buffer` output signal `saved` in libobs does not pass the filename in its callback data. In `main.cpp`, `on_replay_saved` automatically scans `g_cap.output_dir` for the most recently written `.mkv` matching the save timestamp, guaranteeing the exact saved file path is returned to the client.
4. **Interactive Window Station Attachment:**
   - Because `CaptureHost.exe` is launched without a console window (`CreateNoWindow = true`), it explicitly attaches to `WinSta0` and `default` desktop in `enumerate_all_windows` and `EnumWindowsProc` filters cloaked windows using `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED)`.
5. **Elevated Windows & Windows UIPI:**
   - Standard non-elevated processes cannot capture elevated windows due to Windows User Interface Privilege Isolation (UIPI). For games running with administrator rights, the user must run `LocalClip.App.exe` as Administrator or select Display/Monitor capture.
