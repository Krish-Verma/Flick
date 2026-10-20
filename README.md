> **Qualification status:** The original v1.0.0 release predates [backend stabilization PR #1](https://github.com/Krish-Verma/Flick/pull/1). It must not be treated as a validated public release. GitHub Windows CI runs the portable WPF/core build and hardware-independent tests; NVIDIA/OBS captures, Discord desktop integration, fresh-install and long soak acceptance still require the owner GPU machine. See [AntiGravity validation handoff](docs/ANTIGRAVITY-VALIDATION-HANDOFF.md).

# Flick (LocalClip)

**Flick** (also developed under the core codebase name **LocalClip**) is a lightweight, offline-first Windows 11 gameplay clipping application replicating the core workflow of SteelSeries Moments:
- Manual arming (starts **DISARMED** by default; no automatic desktop recording or hidden capture).
- Dedicated hotkeys: **`Ctrl+Shift+F9`** to Arm/Disarm capture, **`Ctrl+Shift+F8`** to Save Replay.
- Bounded 30-second rolling replay buffer in system RAM ($\le 256\text{ MiB}$ budget; zero disk writes until you clip).
- Hardware-accelerated NVIDIA NVENC CBR 15 Mbps @ 1080p30 encoding to immutable Matroska (`.mkv`) originals.
- Multi-track WASAPI audio recording (System Audio Loopback + Microphone).
- Integrated minimal WPF review gallery with instant playback, non-destructive trimming, and per-track audio mute/gain adjustments.
- Guaranteed **Discord Export** strictly within byte budgets (default 20 MB = $20,000,000\text{ bytes}$, plus 50 MB and 100 MB presets), including automated safety margin ($\ge \max(5\%, 256\text{ KiB})$), progressive retry penalty loop, three-tier verification (size check, stream probe, full decode corruption test), and atomic file promotion.
- Native drag handle (`DragDrop.DoDragDrop`) and clipboard file copy (`Clipboard.SetFileDropList`) directly compatible with the real Discord desktop client.
- Zero orphan helper processes guaranteed via Windows Job Object confinement (`KILL_ON_JOB_CLOSE`).
- 100% free and offline core: zero accounts, zero telemetry, zero watermarks, and zero paywalls.

---

## Workspace Structure

```
ClipMax/
├── ClipperBlueprint/             # Authoritative blueprint & requirements
│   ├── WINDOWS-CLIPPER-BLUEPRINT.md
│   ├── ANTI-GRAVITY-HANDOFF-PROMPT.md
│   └── PLANNING-REVIEW.md
├── src/
│   ├── LocalClip.App/            # Minimal usable WPF UI (MVVM, MediaElement, Hotkeys)
│   ├── LocalClip.Core/           # Core library (Rules, IPC, Media, Storage, Diagnostics)
│   └── LocalClip.CaptureHost/    # Native C++ capture engine utilizing libobs
├── tests/
│   └── LocalClip.Core.Tests/     # 41 deterministic unit, integration & stress tests
├── tools/
│   ├── ffmpeg/                   # Pinned FFmpeg & FFprobe 9.0.1 Essentials
│   └── obs/                      # Pinned OBS Studio 32.2.2 runtime & libobs headers
├── scripts/
│   ├── build_package.ps1         # Full release packaging script (produces portable ZIP & installer)
│   ├── run_tests.ps1             # Automated test suite runner
│   └── setup_tools.ps1           # Automated tools & runtime download script
├── docs/
│   ├── USER-GUIDE.md             # End-user guide, workflow, shortcuts & troubleshooting
│   ├── ARCHITECTURE.md           # Technical architecture, IPC protocol & security
│   ├── IMPLEMENTATION-STATE.md   # Detailed milestone state & pinned dependency lock
│   ├── TEST-RESULTS.md           # Requirement-level acceptance matrix & benchmarks
│   └── AGENT-HANDOFF-GUIDE.md    # Inspection guide for incoming agents
├── dist/
│   ├── LocalClip-v1.0.0-win-x64.zip  # Standalone portable distribution archive
│   └── SHA256SUMS.txt            # SHA-256 integrity checksums
├── LocalClip.slnx                # Modern XML .NET solution file
├── dependencies.lock.json        # Pinned dependency inventory with hashes
├── LICENSE                       # GNU General Public License v2.0 or later
└── THIRD-PARTY-NOTICES.md        # Open-source license attributions
```

---

## Quick Start for Developers & Agents

### Prerequisites
- **OS:** Windows 10/11 x64 (Tested on Windows 11 Pro 64-bit Build 26300)
- **GPU:** NVIDIA GeForce GPU with NVENC support (Tested on NVIDIA RTX 5060, Driver 617.42)
- **.NET SDK:** .NET 10 SDK (`%LOCALAPPDATA%\Microsoft\dotnet`)
- **C++ Compiler:** MinGW-w64 GCC/G++ 16.2.0 (WinLibs UCRT)

### 1. Run All Automated Tests
```powershell
dotnet test LocalClip.slnx
```
*CI explicitly skips the six Windows GPU/hardware tests. Use `scripts/run_tests.ps1 -Hardware` on the owner PC to run them; never count skipped hardware tests as passing.*

### 2. Run the Application
```powershell
$env:PATH = "C:\Users\krish\AppData\Local\Microsoft\dotnet;$env:PATH"
dotnet run --project src\LocalClip.App\LocalClip.App.csproj
```
The `dist/app` build output is generated locally, not committed. Build the release first or extract the existing release ZIP (which remains unqualified).

### 3. Build the Release Package & Installer
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build_package.ps1
```
This builds `LocalClip.CaptureHost.exe` (MinGW C++ with static runtime), publishes `LocalClip.App` (Release win-x64), bundles OBS/FFmpeg runtimes, creates `dist\LocalClip-v1.0.0-win-x64.zip`, and writes `dist\SHA256SUMS.txt`.

---

## Key Technical Specifications

| Feature | Implementation | Invariant |
|---|---|---|
| **Capture Policy** | Disarmed on launch | `AppSettings.StartArmed => false` enforced via `[JsonIgnore]` |
| **Video Encoder** | `obs_nvenc_h264_tex` | CBR 15 Mbps @ 1080p30 (Fallback: `obs_x264`) |
| **Replay Buffer** | `replay_buffer` | Bounded to 256 MiB in RAM; circular buffer |
| **Audio Capture** | WASAPI Loopback + Mic | Separated tracks: Track 1 = System Audio, Track 2 = Microphone |
| **Process Isolation** | Windows Job Object | `KILL_ON_JOB_CLOSE` ensures child processes terminate with parent |
| **Discord Export** | Hardware NVENC MP4 | Strict budget cap ($20,000,000\text{ B}$) with retry penalty loop |
| **Local Sharing** | OLE Drag & Drop + Clipboard | DropList with virtual file support; instant Discord desktop paste |
| **Storage** | Atomic Write & Recycle Bin | `.tmp` rename for atomic saves; `SHFileOperation` for Recycle Bin |
| **Webhook Security**| Windows DPAPI | `CurrentUser` scope encryption; empty mentions (`allowed_mentions`) |

---

## Documentation Links
- [User Guide](docs/USER-GUIDE.md)
- [Architecture & Design](docs/ARCHITECTURE.md)
- [Implementation State & Dependency Lock](docs/IMPLEMENTATION-STATE.md)
- [Test Results & Acceptance Matrix](docs/TEST-RESULTS.md)
- [Agent Handoff Guide](docs/AGENT-HANDOFF-GUIDE.md)
