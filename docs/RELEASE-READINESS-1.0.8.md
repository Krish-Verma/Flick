# Flick v1.0.8-preview.1 — release candidate readiness

Status: **UNRELEASED, UNSIGNED, physical Windows acceptance pending**. Public Latest remains v1.0.7-preview.1. This document is an engineering acceptance contract, not release approval.

## Scope

The v1.0.8 candidate is based on merged PRs [#55](https://github.com/Krish-Verma/Flick/pull/55) and [#56](https://github.com/Krish-Verma/Flick/pull/56), originally developed from the exact v1.0.7 public source and merged safely into `main`. No change to the native OBS encoder policy, clip file format, default microphone off state, explicit game-auto-record consent, selected source permissions, GPL/FFmpeg notice obligations or Windows installer AppId.

### Replay saving and truthfulness

- Replay control actions record **only** closed origin/action enums, not raw keys, device/process IDs, titles, clip names, paths or credentials, in the bounded local redacted journal.
- A global Replay Toggle command during an already pending native save may not stop the buffer; the UI/tray manual Stop remains explicit. This does not assert that a stray toggle was the proven cause of the owner's prior stopping event.
- Repeated save while a native save request is outstanding is reported as pending, **not** as a second definite failed clip.
- A lost/unconfirmed IPC command acknowledgment keeps the associated save tracked until the terminal native event or confirmed host exit. A transient “unconfirmed” condition is not the same as a terminal failure.
- Actual playable media probing, durable indexing and matching save identity precede the success banner. Optional JPEG thumbnails are generated separately on demand and are not a hard precondition for a validated save.

### Windows audio output/input device selection

- Settings → Audio contains named **Audio output** and **Microphone input** ComboBoxes, Windows-default options and a read-only Refresh action.
- Settings already persisted `SystemAudioDeviceId` and `MicrophoneDeviceId`; exact chosen Windows MMDevice IDs are now staged and passed unaltered to the existing OBS WASAPI source.
- No mic source is opened when microphone capture is disabled; the input picker being populated is not consent. Missing/disconnected selected devices remain explicitly unavailable and cannot silently switch to another device during a new explicit save.
- System playback is not changed by choosing what Flick should record. Whole-device output selection is inactive when Separate App Audio is selected; Game/Voice source identities and privacy remain separate.
- Opening or cancelling Settings must not rewrite devices, alter audio routing or stop an active capture. Saving a live routing/device change preserves the existing confirmation/stop-replay boundary.

## Automated acceptance

After the candidate branch PR is created, record the exact PR head, merge source tree and matching automated run IDs. Required gates:

1. Windows Core/WPF compilation with warnings as errors; run all hardware-independent tests and native-policy/compiler jobs.
2. Real generated-media FFprobe/FFmpeg tests, audio mixing, source immutability and size checks; gated hardware tests reported as **not executed**.
3. WPF renderer with visible Audio category, endpoint controls, currently selected, missing and failed refresh states, and compact/narrow window. Any layout clipping requires correction before claiming visual acceptance.
4. Per-user Setup/portable integrity: owned EXE/DLL product/version metadata, pinned OBS/FFmpeg files, license notices, checksums; test 1.0.4→candidate installed upgrade/restart and user data preservation without recording from the CI runner.
5. Regression tests for in-flight replay stop suppression, no false notification while save is unconfirmed, private diagnostic schema and selected/disconnected endpoint staging/Cancel behavior.
6. Separate review of potential false NotReady outcomes with synthetic correlated terminal events. True real-game diagnosis and hardware acceptance remain with owner.

Do not mark the candidate package verified until every applicable workflow has completed with `success` on the same candidate source. Failed, cancelled or unexecuted checks are not passes.

## User-performed physical gameplay acceptance

On a compatible authorized Windows 11 PC, owner will verify:
- Single physical hotkey press, the actual source and captured game, Replay Ready stability, no unintended Stop, valid indexed MKV and prompt success banner after validation.
- Deliberate Start/Stop hotkey independent of Save and action-origin diagnostics after any failure.
- Selected headset/speakers and microphone inputs record the expected audible signals with no unwanted sources. Confirm Record Microphone off remains silent, explicitly enabling works and unplugging an active device is handled without switching secretly.
- Borderless/exclusive fullscreen notification visibility, hotkey Fn layer, output-device switches, save under load, long-running performance and recovery.
- Discord drag/paste and release installer over previous installed preview.

No physical tests are automatically claimed by GitHub-hosted CI or visual fixtures.

## Publication boundary

The owner's actual testing begins after receiving a testable approved preview release. Preparing/merging this candidate does **not** itself grant approval to publish a public GitHub release or replace Latest. A release must use a fresh immutable version tag, validated checksums, source/run/artifact provenance, separate human approval and conservative unsigned/hardware-pending labeling. Do not overwrite, unpublish, delete or mutate prior version assets or tags.
