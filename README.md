# Flick

**Capture the moment. Skip the bloat.**

Flick is a **free, open-source Windows gameplay clipper** in development. The goal is simple: save the moments you care about, make quick edits, and share them without a heavyweight background application.

We are building Flick for people who want a fast, dependable alternative to bloated gameplay recording tools—with transparency and performance as first-class priorities.

> [!WARNING]
> **In active development — not ready for everyday or public release use.** The current code and `v1.0.0` archives are experimental and predate final hardware acceptance. Please don't treat a successful CI build as proof that recording, audio, exporting, or installation works on every PC.

## What Flick is being built to do

- **Save the last moments of gameplay:** manually enable capture, then press a separate hotkey to save the recent replay (30 seconds by default).
- **Stay lightweight:** bounded rolling capture, GPU-assisted video encoding where supported, and careful CPU, RAM, disk, and process usage.
- **Keep your clips yours:** local MKV originals, non-destructive trimming, and a searchable gallery.
- **Give you control over sound:** microphone and system audio tracks, independent volume/mute controls where available, and honest labels when full per-app isolation isn't supported.
- **Make sharing easy:** export H.264/AAC MP4 clips to a chosen file-size limit and drag or copy the result into Discord.
- **Respect your privacy:** an offline-first core with no required account, ads, telemetry, paywall, or watermark. Optional webhook posting, if enabled, must always be deliberate.

Flick is **manual-first**: no automatic game detection, automatic highlight recording, or hidden capture.

## Where development stands

| Area | Current status |
| --- | --- |
| Windows WPF app, gallery, settings, hotkeys | Implemented; usability and hardware validation ongoing |
| libobs capture host and NVIDIA NVENC replay | Implemented; real-machine acceptance required |
| Local trim, audio mixing, Discord-size exports | Implemented; complete media and Discord validation required |
| GitHub Actions .NET build and non-hardware tests | Running on the stabilization pull request |
| Portable packaging, native build, long-running stability | In qualification; **not release-approved** |
| Polished interface | Planned after the core capture-to-share workflow is reliable |

The current backend fixes and validation work are tracked in **[PR #1 — Backend Stability & CI](https://github.com/Krish-Verma/Flick/pull/1)**. That pull request is deliberately a draft. Its [validation handoff](https://github.com/Krish-Verma/Flick/blob/fix/backend-stability-and-ci-20261019/docs/ANTIGRAVITY-VALIDATION-HANDOFF.md) lists the Windows/GPU, media, Discord, installer, and endurance checks that must be completed before release.

## Platform and approach

Flick currently targets **Windows x64**, with **NVIDIA NVENC** as the initial hardware-acceleration path. Broader GPU support and compatibility are not yet promised.

The main components are:

- `src/LocalClip.App/` — Windows desktop interface (WPF / .NET 10)
- `src/LocalClip.Core/` — recording coordination, media export, storage, diagnostics, and sharing
- `src/LocalClip.CaptureHost/` — native C++ capture process using OBS/libobs
- `tests/` — automated tests, with hardware-dependent checks explicitly separated
- `ClipperBlueprint/` — original product requirements and design decisions

Some internal project names still say **LocalClip** while the product is being renamed to **Flick**.

## Build and test (contributors)

The full capture and distribution pipeline requires a supported Windows development environment. The backend qualification instructions, dependency versions, and known limitations are in [the development handoff](https://github.com/Krish-Verma/Flick/blob/fix/backend-stability-and-ci-20261019/docs/ANTIGRAVITY-VALIDATION-HANDOFF.md) and [the dependency lock](dependencies.lock.json).

For the .NET solution, install the pinned .NET 10 SDK and run:

```powershell
dotnet restore LocalClip.slnx
dotnet build LocalClip.slnx --configuration Release
powershell -ExecutionPolicy Bypass -File scripts/run_tests.ps1
```

The ordinary test run **skips hardware-dependent tests**; skipped tests are not passes. For qualified Windows/NVIDIA hardware testing, after setting up the native host and pinned runtimes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run_tests.ps1 -Hardware
```

To build a portable package, follow the setup instructions and use `scripts/build_package.ps1`. The produced package still requires a clean-install smoke test before being considered ready for distribution.

## Contributing

Flick is intended to become a public, community-usable open-source project. The repository may remain private during early stabilization. Once opened, bug reports, code review, reproducible test evidence, and focused pull requests will be welcome.

Please keep changes practical and performance-oriented. Don't commit personal recordings, Discord webhook secrets, tokens, or unrelated files. Core functionality and reliable tests come before cosmetic UI work.

## License

Flick is intended to be distributed under **[GPL-2.0-or-later](LICENSE)**. See [third-party notices](THIRD-PARTY-NOTICES.md) for dependencies and attribution. Distribution and license-compliance checks remain part of the release process.

---

**Flick — the moment is yours to keep.**
