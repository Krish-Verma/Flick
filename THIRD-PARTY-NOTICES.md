# Flick — Third-party Components and Distribution Review

These components are included or referenced by the project. **This page is not a certification that every license obligation of the actual compiled binary is satisfied.** Verify full source and binary provenance before making the repository or installer public.

## OBS Studio (libobs)

- Pinned source/runtime: OBS Studio 32.2.2, https://github.com/obsproject/obs-studio
- Upstream license: GPL-2.0-or-later, plus separately licensed components in its runtime.
- Flick links a native C++ helper against libobs and loads its runtime modules. A redistribution must include required copyright/license notices and corresponding source obligations for the shipped modules/build. An upstream repository URL alone may not satisfy the distributor's obligations.

## FFmpeg / FFprobe

- Pinned downloaded build: FFmpeg 9.0.1 Essentials (Gyan.dev build), https://ffmpeg.org and https://www.gyan.dev/ffmpeg/builds/
- Licensing depends on **actual configure flags** and linked libraries; do not assume the binary is only LGPL, or that a generic FFmpeg license statement covers every bundled component.
- Before release, run the exact packaged ffmpeg.exe -buildconf, ffmpeg.exe -version and ffprobe.exe -version; record configuration, version, licenses, checksums and sources for that build. Supply corresponding source/licenses as legally required (including GPL components).

## Microsoft .NET / WPF

- Pinned .NET 10 SDK/runtime, https://github.com/dotnet
- Core runtime/project components are distributed under their respective Microsoft/.NET Foundation licensing (primarily MIT). Check license inventories for each version in the self-contained publish.

## MinGW-w64 GCC / WinLibs

- Windows compiler: MinGW-w64 UCRT GCC build described in dependencies.lock.json, https://winlibs.com
- GCC and runtime libraries have separate GPL, LGPL, runtime-library exceptions or public-domain terms. Record required notices for redistributed runtime pieces. Compiler use does not itself imply all compiled application code is GPL-licensed.

## Installer compiler

- Inno Setup 6 by Jordan Russell, https://jrsoftware.org/isinfo.php
- Used **during packaging** to produce the installer. This does not grant a right to misrepresent signed-publisher identity. Inspect installer redistributable and license terms for the exact compiler version.

## Required release record

1. Record each pinned upstream archive URL and verified SHA-256 from dependencies.lock.json, plus hashes of final EXEs / Setup.
2. Include full required license/copyright texts for **all** shipped runtime files; identify any additional bundled libraries, modules, codecs and fonts.
3. Confirm corresponding source availability, complete source/build scripts and any required written offer for each GPL-covered binary distribution.
4. Ensure game screenshots, player artwork and the original Flick logo have authorized redistribution rights. Keep personal game footage out of source/release assets by default.
5. Get owner legal/release review. No automated public release pipeline currently exists.
