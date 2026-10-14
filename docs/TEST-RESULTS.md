# LocalClip Test Results

**Date:** October 14, 2026  
**Test Machine:** Windows 11 Pro 64-bit (Build 26300), AMD Ryzen 5 5600 6-Core, 16 GB Physical RAM, NVIDIA GeForce RTX 5060 (8 GB Dedicated VRAM, Driver 617.42)

---

## 1. M0: Environment, Toolchain, and Runtime Tests

| Test ID | Category | Command / Procedure | Expected | Actual Result | Status |
|---|---|---|---|---|---|
| **ENV-01** | Toolchain | `dotnet --version` | .NET 10.0+ | `10.0.402` (Workloads: WPF / WindowsDesktop) | **PASS** |
| **ENV-02** | Toolchain | `g++ --version` | GCC 14+ UCRT x64 | `16.2.0` (Brecht Sanders MinGW-W64 UCRT) | **PASS** |
| **ENV-03** | Toolchain | `cmake --version` | CMake 3.25+ | `4.4.2` | **PASS** |
| **ENV-04** | Toolchain | `ffmpeg -version` | FFmpeg 6.0+ with NVENC | `9.0.1-essentials_build` (`--enable-nvenc`) | **PASS** |
| **ENV-05** | Toolchain | `ffprobe -version` | FFprobe 6.0+ | `9.0.1-essentials_build` | **PASS** |
| **HW-01** | GPU / NVENC | `ffmpeg -f lavfi -i testsrc=size=1920x1080:rate=30 -t 2 -c:v h264_nvenc -b:v 15M nvenc_test.mp4` | Hardware encode 1080p30 without error | Produced 2,076,834 byte MP4 at 2.55x real-time on RTX 5060 | **PASS** |
| **OBS-01** | Runtime Linking | Compile `test_obs.cpp` with `-lobs` | Link against `obs.dll` | Linked without symbols errors; reported `OBS version: 32.2.2` | **PASS** |
| **OBS-02** | D3D11 Init | `obs_startup` + `obs_reset_video` D3D11 | Return `OBS_VIDEO_SUCCESS` (0) | D3D11 initialized on Adapter 0 (RTX 5060), 1080p30 NV12 | **PASS** |
| **OBS-03** | Audio Init | `obs_reset_audio` 48kHz Stereo | Return `true` | Audio initialized (samples_per_sec: 48000, 2 channels) | **PASS** |
| **OBS-04** | Encoder Discovery | Targeted module loads (`obs-nvenc`, `obs-ffmpeg`, `obs-x264`) | Enumerate hardware & software encoders | Discovered `obs_nvenc_h264_tex`, `jim_nvenc`, `ffmpeg_aac`, `obs_x264` | **PASS** |
| **OBS-05** | Source Discovery | Targeted module loads (`win-capture`, `win-wasapi`) | Enumerate window/monitor & WASAPI sources | Discovered `window_capture`, `monitor_capture`, `wasapi_process_output_capture`, `wasapi_input_capture` | **PASS** |
| **OBS-06** | Output Discovery | Targeted module loads (`obs-outputs`, `obs-ffmpeg`) | Enumerate replay & muxer outputs | Discovered `replay_buffer`, `ffmpeg_muxer` | **PASS** |
| **OBS-07** | Clean Shutdown | `obs_shutdown` | Zero crash, canvas destroyed, clean exit | Canvas destroyed, contexts freed, return code 0 | **PASS** |

---

## 2. Resource Baselines Observed During M0

- **Idle System Physical RAM:** ~7.8 GB free out of 16 GB
- **Probe Host Private Bytes during D3D11 & NVENC probe:** ~42 MB
- **Probe Host Working Set:** ~68 MB
- **GPU Dedicated VRAM used by probe:** ~18 MB
- **Clean exit:** All resources released; 0 lingering processes.
