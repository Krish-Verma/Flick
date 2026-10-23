# Windows GPU compatibility

## Release boundary

The **v1.0.5-preview.1 unsigned compatibility preview** adds automatic H.264 selection and bounded CPU fallback. The previous 1.0.4 Design Preview was NVIDIA-only and is retained as a hidden draft for authenticated upgrade-test access. Historical RTX 5060 recordings describe that earlier baseline.

Source a6d2cd880c9716e96e9957279b4848eb8d3ca076 passed Windows compilation, deterministic policy/unit tests, five real synthetic-media workflows and WPF rendering. This does not qualify actual capture on NVIDIA, AMD, Intel, hybrid or CPU configurations. No row below has fresh physical acceptance for 1.0.5 yet. Record the exact package and tested machine/driver combination before marking any row qualified.

## Candidate behavior and requirements

Flick prefers a usable hardware H.264 encoder and reports the encoder actually
selected by its capture worker. NVIDIA NVENC, AMD AMF and Intel Quick Sync paths
depend on registration and successful initialization with the installed drivers;
a vendor name or an installed DLL alone is insufficient. The capture engine is
still the bundled OBS runtime, not a new platform port.

When hardware paths are unavailable and CPU fallback is allowed, the candidate
uses x264 with a maximum output of **1280x720 at 30 fps**. This trades output
resolution/frame rate for a bounded compatibility profile. It can still consume
substantial CPU and reduce game performance. The fallback is enabled by default;
disable it in Recording settings to require a usable hardware path instead.
The CPU profile also caps video bitrate at 6,000 Kb/s, uses the ultrafast preset
and limits x264 to one or two worker threads; these bounds are not an FPS guarantee.
There is no claim of equal image quality, FPS impact or resource use across GPUs.

Sharing also retries with CPU H.264 if the NVIDIA export encoder cannot run.
That compatibility retry does not depend on the separate hardware-overshoot
preference. Export still validates the size cap, media streams and complete decode
before promoting a temporary output to a shareable file.

The target remains **Windows 11 x86-64**, a working Direct3D 11 graphics/capture
stack and current manufacturer graphics drivers. Software encoding replaces the
video compression path; it still needs that graphics stack. Remote sessions,
virtual machines and systems with only a basic display driver require their own
evidence and may be unable to record. OBS itself distinguishes system eligibility
from actual recording capacity. [OBS system requirements](https://obsproject.com/kb/system-requirements),
[hardware encoder guidance](https://obsproject.com/kb/hardware-encoding).

OBS's Windows modules and probe helpers import Microsoft C++ runtime libraries,
including `MSVCP140.dll`, `VCRUNTIME140.dll` and `VCRUNTIME140_1.dll`. These are not
provided by Flick's self-contained .NET publish or the pinned OBS ZIP. Install
the current **Visual C++ v14 x64 Redistributable** from
[Microsoft's official downloads page](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
when it is absent. The required runtime must be at least as recent as the build
tools used for the components. This slice does not automatically install it,
accept licensing terms or request administrator access. A clean-machine install
with prerequisites verified remains an acceptance case.

## Hardware acceptance matrix

| Setup | Candidate path | Candidate acceptance |
|---|---|---|
| NVIDIA with working NVENC H.264 | NVENC hardware | Pending real recording, decode, audio and gameplay measurement |
| AMD Radeon with working AMF H.264 | AMF hardware | Pending on an AMD-only Windows 11 machine |
| Intel integrated graphics with Quick Sync | QSV hardware | Pending on an Intel-only Windows 11 machine |
| Intel Arc with Quick Sync | QSV hardware | Pending on an Arc Windows 11 machine |
| Laptop or desktop with multiple GPU vendors | Usable registered hardware path; CPU fallback if allowed | Pending across display/GPU routing and power modes |
| No usable hardware encoder, compatible graphics stack present | x264 CPU compatibility, at most 720p/30 | Pending on a real machine with CPU/FPS measurements |
| CPU fallback disabled and no usable hardware encoder | Refuse recording with an actionable error | Pending negative-path acceptance |
| Windows 10 | No qualification claim | Pending separate OS acceptance |
| Windows ARM/Snapdragon, 32-bit Windows, macOS or Linux | Outside this Windows x64 build | Not qualified; not added by this work |

DRM-protected video, protected desktop surfaces, anti-cheat restrictions and
certain exclusive-fullscreen games can block capture independently of the
encoder. Software fallback does not bypass them. Test HDR/SDR appearance,
multi-monitor framing, audio synchronization and the intended game; successful
file decoding alone does not establish that the intended pixels were captured.
Display recording can include notifications and other visible windows.

## Pinned runtime and package checks

`setup_tools.ps1` extracts the complete, SHA-256-pinned OBS **32.2.2** Windows x64
archive. `build_package.ps1` copies its full runtime tree. Do not prune that tree
to only the currently selected GPU's plugin.

The shared `scripts/check_obs_runtime.ps1` gate runs after setup, before packaging,
after copying the runtime and during distribution verification. It refuses missing,
empty, malformed or wrong-architecture required Windows binaries, including:

- `obs-nvenc.dll`, `obs-qsv11.dll`, `obs-x264.dll` and `obs-ffmpeg.dll` in
  `obs-plugins/64bit`. AMF is part of `obs-ffmpeg` on Windows x64.
- `obs-nvenc-test.exe`, `obs-amf-test.exe`, `obs-qsv-test.exe` and
  `obs-ffmpeg-mux.exe` alongside the capture worker in `bin/64bit`.
- The pinned FFmpeg DLL set, `libx264-164.dll`, threading/transport dependencies,
  capture/audio plugins, graphics modules, plugin data and 32/64-bit game hooks.

The driver-supplied NVIDIA/AMD/Intel implementation libraries are not substitutes
for those packaged modules and are not copied out of a developer's driver install.
The shipped QSV module/helper have no separate VPL DLL import in this pinned
archive. These checks establish payload completeness and architecture, not GPU
availability, Windows trust or capture correctness. Setup continues to fail when
a pre-existing OBS directory is partial; restore it from the pinned source rather
than bypassing the gate.

Module layout was checked against the official ZIP central directory; required
native dependencies were checked by static PE import inspection. This audit read
bounded archive ranges, did not run downloaded code and did not treat upstream
metadata as a hash verification of the whole archive. Installation still verifies
the full pinned archive checksum. Primary definitions:
[QSV module/helper](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-qsv11/CMakeLists.txt),
[x264 module](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-x264/CMakeLists.txt),
[AMF/FFmpeg module](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-ffmpeg/CMakeLists.txt),
[NVENC module/helper](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-nvenc/CMakeLists.txt).

## Exact-machine recording procedure

Use the repository's pinned .NET SDK and a package built from the candidate's
exact source. On the owner's Windows desktop, explicitly choose a monitor whose
contents may be recorded. The test disables microphone capture but records the
selected display and its system audio; review and retain the resulting evidence.

The manual [test_gpu_compatibility.ps1](../scripts/test_gpu_compatibility.ps1)
runner records automatic selection and forced CPU compatibility, checks the
reported encoder, probes both MKVs, exports size-capped H.264/AAC MP4s and
performs complete FFmpeg decodes. Pass
the expected exact OBS encoder ID for that machine; do not accept an unplanned
CPU fallback as proof that a vendor's hardware encoder worked. Example:

```powershell
./scripts/test_gpu_compatibility.ps1 -PackageDirectory 'C:\Flick-candidate' -ExpectedEncoder 'obs_nvenc_h264_tex' -MonitorIndex 0
```

Expected primary IDs are `obs_nvenc_h264_tex` (NVIDIA), `obs_qsv11_v2` (Intel)
and `h264_texture_amf` (AMD). `obs_x264` is the CPU encoder. Vendor IDs containing
`soft` or `fallback` still use that vendor's hardware with a different input path;
they are not x264. If one of those paths is the intended test, pass its actual
reported ID and record that distinction. The auto order is NVIDIA texture, Intel
texture, AMD texture, registered vendor compatibility paths, then allowed x264.
The reported ID identifies the successful public OBS registration. OBS may
internally reroute its input path without exposing a different ID through the
public API; the result does not prove a particular internal texture/copy path.
Definitions: [Flick encoder policy](../src/LocalClip.CaptureHost/encoder_policy.hpp),
[OBS NVENC IDs](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-nvenc/nvenc.c),
[OBS QSV IDs](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-qsv11/obs-qsv11.c),
[OBS AMF IDs](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-ffmpeg/texture-amf.cpp),
[OBS x264 ID](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/obs-x264/obs-x264.c).

`PackageDirectory` must contain the extracted Flick payload, not its parent ZIP
directory. The evidence directory records source/host hash, OS and adapter/driver
metadata, test results and MKVs. Use a separate run for every matrix row and
display/GPU-routing configuration. Inspect the clips for actual framing, motion,
audio synchronization and colors, then measure CPU, RAM, GPU and gameplay FPS.
Retain failed runs too. Stop/restart, repeated saves, exports and imports must
still pass; none of those checks are replaced by a successful CI build.
