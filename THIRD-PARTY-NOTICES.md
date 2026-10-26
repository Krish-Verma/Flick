# Flick — Third-party Components and Distribution Review

These components are included or referenced by the public project. **This page is not a certification that every license obligation of the actual compiled binary is satisfied.** This notice describes the 1.0.5-preview.1 development candidate, not the older 1.0.4 download's dependency inventory. Complete source/license provenance remains a release obligation.

## OBS Studio (libobs)

- Pinned source/runtime: OBS Studio 32.2.2, https://github.com/obsproject/obs-studio
- Upstream license: GPL-2.0-or-later, plus separately licensed components in its runtime.
- Flick links a native C++ helper against libobs and loads its runtime modules. A redistribution must include required copyright/license notices and corresponding source obligations for the shipped modules/build. An upstream repository URL alone may not satisfy the distributor's obligations.

## FFmpeg / FFprobe

- Pinned build: **FFmpeg 9.0.2 Essentials, Gyan.dev Windows x64 static GPLv3 build**. See [upstream release](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2), its [FFmpeg source anchor 946fcce07b](https://github.com/FFmpeg/FFmpeg/commit/946fcce07b), and the publisher's [build information](https://www.gyan.dev/ffmpeg/builds/). The dependency lock records `GPL-3.0-or-later`; the exact bundled text and each included component's terms govern redistribution.
- Setup preserves the archive's exact `LICENSE` and `README.txt` as `tools/ffmpeg/UPSTREAM-LICENSE.txt` and `UPSTREAM-README.txt`. Packaging copies both beside `ffmpeg.exe` and `ffprobe.exe`. Their individual SHA-256 hashes and executable/archive hashes are pinned in `dependencies.lock.json` and checked by `check_ffmpeg_runtime.ps1`. These notices are not rewritten summaries.
- Do not describe this GPL-enabled package as LGPL-only. FFmpeg's [pinned licensing explanation](https://github.com/FFmpeg/FFmpeg/blob/946fcce07b/LICENSE.md) describes how GPL features, version-3 options and linked libraries affect licensing. Preserve the publisher's configuration/component inventory and capture the packaged tools' `-buildconf` and `-version` output for each release.
- Complete corresponding source for the **exact shipped binary and linked components**, applicable patches and build scripts must be available by a method satisfying their licenses. A commit URL, upstream link, checksum or this notice alone does not establish that obligation. A verified complete source bundle or other valid source-delivery arrangement remains to be completed before claiming distribution compliance. Original Flick source remains GPL-2.0-or-later; this notice does not relicense it or upstream components.
- JPEG acknowledgment: This software is based in part on the work of the Independent JPEG Group. Preserve additional component-specific credits from the complete source/license inventory.
- OBS 32.2.2 contains separate FFmpeg-related DLLs. Upgrading the standalone tools does not upgrade those separately pinned runtime components.

## Microsoft .NET / WPF

- Pinned .NET 10 SDK/runtime, https://github.com/dotnet
- Core runtime/project components are distributed under their respective Microsoft/.NET Foundation licensing (primarily MIT). Check license inventories for each version in the self-contained publish.

## MinGW-w64 GCC / WinLibs

- Windows compiler: MinGW-w64 UCRT GCC build described in dependencies.lock.json, https://winlibs.com
- GCC and runtime libraries have separate GPL, LGPL, runtime-library exceptions or public-domain terms. Record required notices for redistributed runtime pieces. Compiler use does not itself imply all compiled application code is GPL-licensed.

- Flick's native capture helper statically includes the MinGW-w64 winpthreads runtime. Its complete upstream copyright, MIT permission and derived-code terms are included in [docs/licenses/WINPTHREADS-COPYING.txt](docs/licenses/WINPTHREADS-COPYING.txt), from [upstream revision 0ed7805292c3e7e8ee6342b08c115be4276f3ffa](https://github.com/mingw-w64/mingw-w64/blob/0ed7805292c3e7e8ee6342b08c115be4276f3ffa/mingw-w64-libraries/winpthreads/COPYING). Users do not need to install MinGW or obtain a separate pthread DLL.

## Installer compiler

- Inno Setup 6 by Jordan Russell, https://jrsoftware.org/isinfo.php
- Used **during packaging** to produce the installer. This does not grant a right to misrepresent signed-publisher identity. Inspect installer redistributable and license terms for the exact compiler version.

## Required release record

1. Record each pinned upstream archive URL and verified SHA-256 from dependencies.lock.json, plus hashes of final EXEs / Setup.
2. Include full required license/copyright texts for **all** shipped runtime files; identify any additional bundled libraries, modules, codecs and fonts.
3. Confirm corresponding source availability, complete source/build scripts and any required written offer for each GPL-covered binary distribution.
4. Ensure game screenshots, player artwork and the original Flick logo have authorized redistribution rights. Keep personal game footage out of source/release assets by default.
5. Complete owner legal/release review for the exact candidate. Public preview workflows exist; publication and passing CI do not certify legal compliance, trusted signing or hardware acceptance.
