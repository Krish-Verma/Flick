# Flick 1.0.7 Preview 1 — Product Features Candidate (UNRELEASED · UNSIGNED)

This is an **unreleased development candidate**, not an announced download. It prepares the combined A–E work for a distinct `v1.0.7-preview.1` version. Earlier focused checks and the first integration batch do not qualify this exact versioned source. Final Windows build/tests, rendering, installer/package checks and applicable physical acceptance remain required. Publishing a new release or replacing Latest requires fresh explicit owner approval.

## Candidate changes from 1.0.6

- **Recording health and diagnostics:** persistent recording/save errors, actual encoder/profile and retained-buffer estimates, bounded local diagnostic events, and a consented recording test that saves through the real validation/index pipeline. Diagnostic audio is off unless explicitly included; a successful test is specific to that tested source/setup.
- **Optional game replay:** disabled by default; approve observed executable identities and per-game replay profiles in Games settings. Enabled automation may start replay when an eligible approved game is present, including after a later Flick launch. Stop Replay pauses that running game session. Missing/ambiguous windows do not become a display capture.
- **Actual app audio routing:** choose explicit whole-device System Audio or selected Game/Voice process trees. Process identities include PID, executable path and creation time; app restarts require refreshing selection. Unavailable app audio refuses/stops capture rather than silently selecting system loopback. Microphone remains separately opt-in. Attachment, observed signal and proven isolation are different claims.
- **Nondestructive editing and organization:** favorites, tags, game grouping, markers, filters/sorting, timeline controls, optional bounded frames, mute/gain/default mix controls and on-demand mix preview. Combined audio is an alternative to its dedicated tracks and is not mixed with them twice. Original, portrait and square exports fit the complete frame.
- **Share presentation:** a clip preview/title/duration, explicit export/cancel/stale/offline states, and whole-tile drag/Copy File for the validated local MP4. Optional webhook posting retains destination confirmation. Windows file data is implemented; actual Discord desktop acceptance remains a physical test.

Manual replay starts off. The default installation has game automation off. An explicitly saved automatic-game setting can start recording when its approved game becomes eligible; this candidate must not be described as always disarmed under every saved configuration.

## Installation and updates — after separate publication approval

Use the matching **Flick-Setup.exe** for per-user installation over an existing installed preview, without uninstalling first. The portable filename is **Flick-v1.0.7-win-x64.zip**; keep every bundled OBS/FFmpeg/.NET file together. Installed copies can check **Settings → General → Software Updates**, with previews enabled, once a matching release actually exists. Older builds may require a one-time manual installer upgrade. Portable users must quit Flick before replacing the extracted program folder.

Settings, clip index and automation state are separate from program binaries. Imported media and original recordings are preserved during normal upgrade and data-preserving uninstall. Interactive removal of app-owned preferences/index/cache is a separate opt-in; explicit Delete Original remains a distinct destructive action with confirmation.

Targets Windows 11 x64 with working Direct3D11 graphics, current GPU drivers and the Microsoft Visual C++ v14 x64 Redistributable. The documented process-loopback API floor is Windows build 20348; successful activation and suitable app process structure are still required. x264 compatibility recording, when allowed, is capped at 1280x720 and 30 fps and can increase CPU load or reduce game performance.

## Qualification and trust limits

**UNSIGNED:** No trusted publisher signature or Microsoft Store certification is claimed. Smart App Control may block the installer or application. SHA-256 checksums verify transfer consistency rather than publisher trust. Keep Windows security protections enabled; signing and Store distribution are separate workstreams.

**ACCEPTANCE PENDING:** Hosted tests and generated tones do not establish universal NVIDIA/AMD/Intel/hybrid capture, protected-game compatibility, microphone/endpoint isolation, actual Discord acceptance or performance. The isolated physical Core test receipt was not a GPU/capture qualification. The attempted interactive recording bridge failed with privilege error 1314 before creating a capture child; it is not a physical recording pass. Do not change privileges/security to force that test.

Final exact-source Windows CI, all expected media/App fixtures, visual/accessibility checks, upgrade/uninstall preservation, process-tone isolation, real endpoint/Discord tests and gameplay FPS/1% lows/CPU/GPU/endurance measurements remain individually tracked. No leak-free, zero-overhead, all-GPU or ready-to-ship claim is made. See [candidate readiness](../docs/RELEASE-READINESS-1.0.7.md) and the [A–E plan](../docs/product-phase/MASTER-PLAN.md).

Original Flick source remains GPL-2.0-or-later; bundled components retain their separate licenses, notices and corresponding-source obligations. Local capture/edit/export requires no account and does not automatically upload clips.

These notes do not authorize publication. A future publisher must pin the final source commit/tree, successful same-source Windows/render/package runs, immutable artifact digest, reviewed Setup hash and verified downloads. Previously published 1.0.6 tags, releases, assets and approval evidence remain unchanged.
