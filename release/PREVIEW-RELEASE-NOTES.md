# Flick 1.0.5 — Windows compatibility candidate (UNSIGNED)

This candidate adds automatic NVIDIA NVENC, AMD AMF and Intel Quick Sync H.264 recording, with an optional CPU fallback capped at 720p/30 fps. Sharing retries with CPU encoding if NVENC is unavailable. The existing purple interface, refined toolbar icons, subtle gradients and compact share panel remain.

Replay readiness now follows observed decodable packets, and quitting waits for accepted replay saves and library indexing. Damaged library indexes are preserved instead of overwritten. Imported portrait/rotated and odd-sized video receives compatible export dimensions. FFmpeg is updated to the pinned 9.0.2 build with its exact upstream license and build notices.

## Installation and updates

Use Flick-Setup.exe for a per-user installation. Existing installed Flick 1.0.4 copies can check Settings → General → Software Updates once this candidate is published. Older copies without the installer updater need one manual upgrade. Portable copies require quitting Flick and replacing the extracted application folder.

Windows 11 x64, a working Direct3D 11 driver, and Microsoft Visual C++ v14 x64 runtime are required. Keep the bundled tools together. Verify the downloaded installer against SHA256SUMS.txt from the same release.

## Qualification

This is an unsigned compatibility candidate. Windows may block it; SHA-256 checks do not confer publisher trust. SignPath approval and a trusted signed build are separate gates. No new NVIDIA, AMD, Intel, hybrid or CPU hardware recording row is qualified by hosted CI. See docs/WINDOWS-GPU-COMPATIBILITY.md and docs/RELEASE-READINESS-1.0.5.md for current evidence and outstanding acceptance.

The release workflow must record the exact source SHA, successful CI runs, final asset hashes and installer upgrade receipt before publishing these notes. This source file does not itself announce a published release.
