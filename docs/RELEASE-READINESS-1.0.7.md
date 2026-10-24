# Flick 1.0.7-preview.1 candidate readiness

Status: **unreleased, unsigned, not yet qualified for shipment**. This document prepares a unique version for the combined A–E implementation. It records required gates, not a claim that they passed. Publishing any new release or replacing GitHub Latest requires fresh explicit owner approval after review of the concrete final artifacts.

## Version and preservation boundary

- Candidate semantic version: `1.0.7-preview.1`; numeric file/assembly version: `1.0.7.0`; installer version: `1.0.7`.
- Proposed future tag: `v1.0.7-preview.1`; portable package: `Flick-v1.0.7-win-x64.zip`; installer asset: `Flick-Setup.exe`.
- This is source/packaging preparation only. No tag, release, GitHub publication, signing submission or installed profile is changed by this work.
- Published1.0.6 approval/source pins and historical evidence remain unchanged. Do not reuse the 1.0.6 tag, replace its assets, or redirect its approved publisher to this candidate.
- Runtime/tool pins remain unchanged: OBS 32.2.2, FFmpeg 9.0.2 essentials, .NET SDK 10.0.402/runtime 10.0.13, and the existing native toolchain declarations. Only the application's version changes in the dependency manifest.
- The conventional installer retains its AppId/per-user install path and data-preserving defaults. Existing settings, index, game suppression state, originals/imports and exports remain separate from binary replacement. Optional profile removal and explicit Delete Original retain their own confirmation boundaries.

## Included behavior requiring acceptance

| Work | Implementation to verify | Qualification still required |
| --- | --- | --- |
| A — health/test/recovery | Correlated saves, actual profile/buffer health, bounded allowlisted local logs, consented real recording test and cleanup | Exact-source Windows/App tests; physical hotkeys, recording/save recovery and interruption |
| B — optional game replay | Off by default; observed approved executable identities, explicit profiles, serialized detection, exact window binding and manual override/session suppression | Settings interactions; game launch/exit/restart/minimize/focus/ambiguity; no unintended display/audio capture |
| C — real app audio | Explicit whole-device or exact Game/Voice process trees; independent microphone consent; actual source buses/status; editor/preview/export mixing | Independent process-tone capture, source loss/restart, Discord process structure, headphones/speakers/default-output changes and opt-in mic |
| D — editing/organization | Favorites/tags/game/markers, cached search, timeline controls, bounded optional frames, fitted export shapes and saved edit state | Windows keyboard/mouse/layout tests, legacy/offline/imported index roundtrip and original-byte preservation |
| E — Share | Still preview, metadata/state hierarchy, real Windows file data, explicit webhook destination | Actual Discord desktop drag/paste, keyboard/accessibility/high contrast, failed/stale/offline and minimum-window behavior |

Manual replay starts off. Approved-game replay is a separate opt-in that may start automatically when an eligible approved game is present, including on a later launch. Documentation and tests must preserve this distinction.

## Required exact-source release evidence

The first PR49 integration batch and earlier per-lane focused passes are discovery evidence. Any runtime/UI fix or this version change produces a different source; record a fresh final candidate and applicable same-source results before approval.

| Gate | Status at version preparation | Evidence required before publication consideration |
| --- | --- | --- |
| Final source commit/tree | Pending final integration and version commit | Immutable full commit/tree; clean executable input inventory |
| Windows warnings-as-errors build and native link | Pending on this final candidate | Successful .NET/WPF build and GCC native build including COM link dependency; correct four-owned-PE product/version metadata |
| Independent/Core/App tests | Pending combined batch | Expected executed counts with 0 failures; skipped hardware cases explicitly separated; single-Application WPF assertion helpers actually invoked |
| Synthetic media | Pending combined batch | Exactly 8 expected generated-media cases execute/pass with pinned FFmpeg; full MP4 decode, contribution/mute/gain/no double mix, aspect/trim/cancellation and unchanged original bytes |
| WPF visuals/interactions | Pending final rendering | Actual controls at compact/split/minimum layouts; Audio/Games/organization/Share; keyboard, overflow, high contrast; raster scaling distinguished from real DPI acceptance |
| Installer/package | Pending final candidate | Complete pinned runtime inventory, checksums, Setup/ZIP/host hashes, upgrade wait/restart, retained user data and uninstall allowlist behavior |
| Physical capture/audio | Not qualified | Authorized unlocked owner session; selected synthetic window/processes, real saved/decoded media and confirmed owned-process cleanup; no private microphone/audio substitution |
| Performance/endurance | Not measured for combined candidate | Repeatable startup/readiness/save/export latency, idle/recording CPU/RAM/GPU, equivalent gameplay FPS/1% lows and 2-hour/8-hour runs with exact hardware/driver/profile |
| Signing/Store | Separate and pending | Approved signing identity, trusted signatures/SAC acceptance or separate Store certification; no security bypass |
| Publication | Not authorized by version preparation | Fresh explicit approval of the exact reviewed source, artifacts, disclosures and proposed release action |

Do not infer a strict single-worker guarantee or measured overhead merely from cancellation code and pixel/time/file caps. The final review must confirm each thumbnail/filmstrip/compatibility/mix/export route participates in the intended ownership/transition gates; tests and real traces must show the practical limits.

## Physical evidence limits

Earlier isolated Windows Core execution did not exercise GPU capture, user-session1 interaction, Discord or audio endpoints. A recording qualification attempt failed at same-owner session creation with `ERROR_PRIVILEGE_NOT_HELD` 1314 before launching the capture child. Preserve that refusal. No privilege/security/driver changes are authorized by this candidate plan. Hosted generated tones prove the mixing mathematics; they do not prove Windows process-loopback isolation.

The separate recording and audio harness payloads must be built from matching source, archive/file hashes verified, and resource/session admission performed before separately authorized execution. The audio harness uses only its owned 440/880/unselected 1320 Hz tone processes with mic/device-loopback off. Building or staging a harness is not a passing physical test.

## Trust, licensing and distribution

This candidate remains an unsigned developer preview. GitHub Latest is a catalogue position, not a Windows trust or stable-quality certification. Checksums are not signatures; do not ask users to lower Smart App Control or other protections. Keep current signed-QA identity/environment guards, upstream binary hash preservation and own-binaries-only signing allowlist intact. SignPath and Microsoft Store work remain separate from this version preparation.

Original Flick code remains GPL-2.0-or-later. OBS/libobs, FFmpeg, .NET and other bundled components retain their own licenses and notices; complete redistribution/corresponding-source obligations remain a release gate. Keep historical releases/tags and approval files unchanged. If publication is later authorized, create a new pinned publisher/approval record rather than reusing the immutable 1.0.6 one.
