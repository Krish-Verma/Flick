# C — Real app audio and mix editing

Implemented in the C branch; Windows and physical acceptance remain separate from implementation. No new release has been published by this work.

## Capture contract

Settings → Audio offers **System Audio**, explicitly labelled whole output-device loopback, or **Separate App Audio**. Separate mode accepts one Game and/or Voice app selected from refreshed running windows. Each selection freezes PID, full executable path and process creation time. Old `GameProcessId`/`DiscordProcessId` settings alone authorize nothing. App restarts require a fresh selection; overlapping Game/Voice process trees are rejected. Microphone consent remains independent and off by default.

Separate mode uses an exact-PID OBS audio input backed by Windows process-loopback capture. Its source never resolves a new window by title, reconnects to another process, or replaces a failed app source with device loopback. Unavailable identity, activation failure, unsupported OS, or a lost source refuses/stops capture. To record device audio instead, the user must select System Audio explicitly. Changing a running recording's audio routing asks before discarding unsaved replay and stops that recording after the settings save.

OBS 32.2.2's [`wasapi_process_output_capture`](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-wasapi/win-wasapi.cpp) resolves windows by title/class/executable. Its `get_hooked` result exposes those strings, rather than the actual HWND or PID. That cannot prove the requested process instance when identities collide or restart, so Flick does not use the resolver as evidence of isolation. Flick's small source uses the same documented Windows `ActivateAudioInterfaceAsync` process-loopback mechanism, with a retained process handle and no replacement resolver. The pinned OBS runtime still owns audio mixing, AAC encoders and replay muxing.

Microsoft documents Windows build **20348** as the supported minimum for [process-loopback activation](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_activation_params). OBS experimentally registers its source starting at19041; Flick uses Microsoft's documented floor and still requires successful activation. The [process parameters](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params) include a process and its children. Game/Voice are user-assigned roles, not detection of game sounds or individual people. Selecting a window whose audio is produced by a sibling process may produce silence; no sound is fabricated or silently replaced. Protected/elevated apps and endpoint/session restrictions require real acceptance testing.

## Actual buses and status

Mixer0 is Combined Audio. Each real source receives mixer0 plus one dedicated bus, in order:

| Mode | Dedicated buses |
| --- | --- |
| System Audio | System Audio (whole device), optional Microphone |
| Separate App Audio | selected Game Audio (process tree), selected Voice Audio (process tree), optional Microphone |
| Audio-off diagnostic | silent AAC bus only; no loopback or microphone source |

Only selected sources create tracks. There are at most four encoded tracks (Combined plus Game/Voice/Mic), using the existing48kHz stereo AAC pipeline. Source names/counts are not isolation evidence. `audio_mode_requested`, `audio_mode_actual` and `audio_sources` report each real source's kind, attachment state, selected PID and whether a nonzero signal has ever been observed. **Attached** means the exact process audio client started; **sound observed** means samples exceeded a small nonzero threshold, not that the correct game/voice content has been physically verified. System/mic status identifies device routing without pretending to have measured a live level.

Recording Health displays this summary. Lost exact process audio faults and stops the replay pipeline. The recording self-test freezes the same app-audio configuration in its consent plan; it cannot silently turn a separated test into whole-device capture.

## Editor, preview and export

Original MKVs remain immutable. Correlated native save metadata assigns explicit track roles; imported titles and track counts cannot establish those roles. Defaults select real dedicated buses and mute Combined. Independent mute/gain controls and Reset Mix are retained. Combined is an alternate complete mix: selecting it excludes dedicated buses from both export and mix preview, preventing the same source being mixed twice.

**Preview Mix** generates one bounded, on-demand H.264/AAC playback copy with the selected trim and the same production audio filter as export. It does not start parallel players or background decoders. The preview is capped at3minutes/120MB, uses one confined worker, is cancelled on selection/edit/capture change, and is invalidated when the mix changes. A VM-owned operation retains its task through cancellation; capture and quit await `CancelAudioPreviewAndWaitAsync`, while diagnostics, export and updater gates reserve against its busy state. FFprobe and preview FFmpeg kill/close their confined job and await actual process exit; an unconfirmed exit retains a safety fence. Original playback still plays the original recording; Export Preview plays the actual MP4. All-muted export/preview generates silent stereo rather than silently re-enabling a source.

## Evidence and remaining gates

Portable tests exercise exact identity validation, no legacy PID consent, microphone default, audio-off diagnostics, frozen self-test routing, actual status parsing, track provenance, independent mute/gain/defaults and Combined precedence. Real generated440Hz/880Hz signals pass through the production filter, verify spectral contributions and silence, encode/decode an H.264/AAC MP4, and preserve original bytes. This verifies mixing, **not capture isolation**.

The separate `Flick.AudioQualification` executable is an explicit Windows owner-session harness. `scripts/build_audio_qualification.ps1` assembles it in disposable Windows CI with a matching native/OBS/FFmpeg package, source/tree manifest and archive hash; assembly does not play or capture audio. After the integration lead verifies that payload and admits an unlocked authorized interactive session, run:

```
Flick.AudioQualification.exe --run C:\absolute\new-owned-output-directory obs_nvenc_h264_tex
```

The output directory must be new. The harness launches only its three owned synthetic-window processes:440Hz Game,880Hz Voice, and unselected1320Hz. Microphone and device loopback remain off. It saves through the real native replay pipeline, decodes each dedicated/Combined track, checks rejection of the other/unselected tones, closes Voice and demands capture fault/stop, then confirms owned helper/tone process exit. All originals and a JSON receipt are retained. A passing one-machine test still does not qualify every GPU, endpoint or Discord session. Never run this through a locked/session0 desktop, change privileges/security to force it, or substitute real user audio. The existing A4 same-owner bridge must be separately authorized/qualified for this new executable; the harness itself does not cross sessions or change tokens.

Pending physical gates: independent source-tone capture; Discord's actual process tree; headphones/speakers/default-output changes; app exit/restart/PID reuse; microphone off/on with explicit consent; permissions/protected apps; capture startup/save latency, CPU/RAM and endpoint overhead. Windows hosted build/Core/App tests, staged WPF renders, real generated-media mixing and installer regression passed on the merged tree; see FINAL-REPORT.md. This branch makes no hardware-isolation, signing or release-readiness claim.
